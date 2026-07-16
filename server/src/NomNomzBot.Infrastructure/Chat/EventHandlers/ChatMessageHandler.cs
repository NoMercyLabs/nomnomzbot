// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.RateLimiting;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Commands.Events;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Chat.EventHandlers;

/// <summary>
/// Hot-path handler for every incoming chat message.
/// 1. Checks for command prefix (!commandname)
/// 2. Looks up command in the in-memory ChannelRegistry (no DB hit)
/// 3. If no custom command found, checks IBuiltinCommandCatalog (code-defined builtins)
/// 4. Validates permission level: broadcaster > mod > vip > sub > viewer
/// 5. Checks global and per-user cooldowns via ICooldownManager
/// 6. For response-type commands: resolves template variables, sends message
/// 7. For pipeline-type commands: delegates to IPipelineEngine
/// 8. For builtin commands: delegates to IBuiltinCommand.ExecuteAsync
/// </summary>
public sealed class ChatMessageHandler : IEventHandler<ChatMessageReceivedEvent>
{
    private readonly IChannelRegistry _registry;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICooldownManager _cooldowns;
    private readonly IChatProvider _chat;
    private readonly IPipelineEngine _pipeline;
    private readonly IBuiltinCommandCatalog _builtins;
    private readonly ITemplateResolver _templateResolver;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ChatMessageHandler> _logger;

    public ChatMessageHandler(
        IChannelRegistry registry,
        IServiceScopeFactory scopeFactory,
        ICooldownManager cooldowns,
        IChatProvider chat,
        IPipelineEngine pipeline,
        IBuiltinCommandCatalog builtins,
        ITemplateResolver templateResolver,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<ChatMessageHandler> logger
    )
    {
        _registry = registry;
        _scopeFactory = scopeFactory;
        _cooldowns = cooldowns;
        _chat = chat;
        _pipeline = pipeline;
        _builtins = builtins;
        _templateResolver = templateResolver;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task HandleAsync(
        ChatMessageReceivedEvent @event,
        CancellationToken cancellationToken
    )
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        // Cooldown manager is keyed by a string channel id; use the tenant Guid's string form.
        string cooldownChannelKey = @event.BroadcasterId.ToString();

        // Increment channel message counter (used by TimerService for activity gating; approximate is fine)
        ChannelContext? channelCtx = _registry.Get(@event.BroadcasterId);
        if (channelCtx is not null)
            channelCtx.MessageCount++;

        string? text = @event.Message?.Trim();
        if (string.IsNullOrEmpty(text) || text[0] != '!')
            return;

        // Parse: !commandname arg1 arg2 ...
        int spaceIdx = text.IndexOf(' ');
        string commandName = (spaceIdx > 0 ? text[1..spaceIdx] : text[1..]).ToLowerInvariant();
        string args = spaceIdx > 0 ? text[(spaceIdx + 1)..].Trim() : string.Empty;

        if (string.IsNullOrEmpty(commandName))
            return;

        ChannelContext? ctx = _registry.Get(@event.BroadcasterId);
        if (ctx is null)
        {
            // Channel missed the startup bootstrap (new channel, or registry was cold).
            // Lazy-load it now so this and subsequent messages process correctly.
            ctx = await EnsureChannelLoadedAsync(
                @event.BroadcasterId,
                @event.TwitchBroadcasterId,
                cancellationToken
            );
            if (ctx is null)
                return;
        }

        ctx.LastActivityAt = _timeProvider.GetUtcNow();
        ctx.SessionChatters.TryAdd(@event.UserId, @event.UserDisplayName);

        // Look up command in in-memory cache (O(1), no DB hit)
        if (!ctx.Commands.TryGetValue(commandName, out CachedCommand? command))
        {
            // Fall back to built-in catalog (code-defined commands like !uptime).
            IBuiltinCommand? builtin = _builtins.Get(commandName);
            if (builtin is null)
                return;

            if (IsBuiltinDisabled(ctx, commandName))
                return;

            if (
                !await HasPermissionAsync(
                    @event,
                    builtin.DefaultMinPermissionLevel,
                    cancellationToken
                )
            )
                return;

            if (_cooldowns.IsOnCooldown(cooldownChannelKey, commandName))
                return;

            if (builtin.DefaultCooldownSeconds > 0)
                _cooldowns.SetCooldown(
                    cooldownChannelKey,
                    commandName,
                    TimeSpan.FromSeconds(builtin.DefaultCooldownSeconds)
                );

            BuiltinCommandContext builtinCtx = new()
            {
                BroadcasterId = @event.BroadcasterId,
                TriggeringUserId = @event.UserId,
                TriggeringUserDisplayName = @event.UserDisplayName,
                TriggeringUserLogin = @event.UserLogin,
                RoleLevel = BadgeLevel(@event),
                Args = args,
                // A reply carries the parent message + author so a built-in can capture it (e.g. !quote add).
                ReplyParentMessageBody = @event.ReplyParentMessageBody,
                ReplyParentUserName = @event.ReplyParentUserName,
                // Personality tone + explicit per-command override (OverridesJson) drive the built-in's
                // response phrasing: override wins, else the tone template, else the built-in's neutral.
                Personality = ctx.Personality,
                CustomResponseTemplate = ctx.BuiltinResponseOverrides.GetValueOrDefault(
                    commandName
                ),
                CancellationToken = cancellationToken,
            };

            Result<string> builtinResult = await builtin.ExecuteAsync(
                builtinCtx,
                cancellationToken
            );

            if (builtinResult.IsSuccess && !string.IsNullOrEmpty(builtinResult.Value))
                await SendResponseAsync(@event, builtinResult.Value, cancellationToken);

            await PublishExecutedAsync(
                @event,
                commandName,
                builtinResult.IsSuccess,
                cancellationToken
            );
            return;
        }

        // Permission check
        if (!await HasPermissionAsync(@event, command.MinPermissionLevel, cancellationToken))
        {
            _logger.LogDebug(
                "Command {Command} denied for {User} in {Channel}: insufficient permission",
                commandName,
                @event.UserDisplayName,
                @event.BroadcasterId
            );
            return;
        }

        // Global cooldown check
        if (command.GlobalCooldown > 0 && _cooldowns.IsOnCooldown(cooldownChannelKey, commandName))
        {
            _logger.LogDebug(
                "Command {Command} on global cooldown in {Channel}",
                commandName,
                @event.BroadcasterId
            );
            return;
        }

        // Per-user cooldown check
        if (
            command.UserCooldown > 0
            && _cooldowns.IsOnCooldown(cooldownChannelKey, commandName, @event.UserId)
        )
        {
            _logger.LogDebug(
                "Command {Command} on user cooldown for {User} in {Channel}",
                commandName,
                @event.UserDisplayName,
                @event.BroadcasterId
            );
            return;
        }

        // Set cooldowns
        if (command.GlobalCooldown > 0)
            _cooldowns.SetCooldown(
                cooldownChannelKey,
                commandName,
                TimeSpan.FromSeconds(command.GlobalCooldown)
            );
        if (command.UserCooldown > 0)
            _cooldowns.SetCooldown(
                cooldownChannelKey,
                commandName,
                TimeSpan.FromSeconds(command.UserCooldown),
                @event.UserId
            );

        _logger.LogInformation(
            "Executing command {Command} for {User} in {Channel}",
            commandName,
            @event.UserDisplayName,
            @event.BroadcasterId
        );

        try
        {
            if (command.Tier == "pipeline" && !string.IsNullOrEmpty(command.PipelineGraphJson))
            {
                // Pipelines gate on `user.role` via SYNCHRONOUS conditions, so the variable must carry
                // the EFFECTIVE role up front — a badge-less Editor or a !permit elevation would
                // otherwise fail user_role conditions it rightfully clears (item 24c).
                Dictionary<string, string> variables = BuildInitialVariables(@event, args);
                variables["user.role"] = await ResolveEffectiveRoleTokenAsync(
                    @event,
                    cancellationToken
                );

                PipelineRequest request = new()
                {
                    BroadcasterId = @event.BroadcasterId,
                    PipelineJson = command.PipelineGraphJson,
                    TriggeredByUserId = @event.UserId,
                    TriggeredByDisplayName = @event.UserDisplayName,
                    MessageId = @event.MessageId,
                    RawMessage = @event.Message ?? string.Empty,
                    InitialVariables = variables,
                };

                PipelineExecutionResult pipelineResult = await _pipeline.ExecuteAsync(
                    request,
                    cancellationToken
                );

                // Stopped is a deliberate Stop action mid-pipeline — the command still did its work.
                await PublishExecutedAsync(
                    @event,
                    command.Name,
                    pipelineResult.Outcome is PipelineOutcome.Completed or PipelineOutcome.Stopped,
                    cancellationToken
                );
            }
            else
            {
                // Simple response command — pick a response (round-robin or random).
                // If the command has no template responses (misconfigured or a builtin key that
                // lives in the Commands table for metadata purposes), fall through to the builtin
                // catalog so the code-defined handler still fires (e.g. !sr, !song, !uptime).
                string response = PickResponse(command.TemplateResponses);
                if (string.IsNullOrEmpty(response))
                {
                    IBuiltinCommand? builtin = _builtins.Get(commandName);
                    if (builtin is null)
                        return;

                    if (IsBuiltinDisabled(ctx, commandName))
                        return;

                    BuiltinCommandContext builtinFallbackCtx = new()
                    {
                        BroadcasterId = @event.BroadcasterId,
                        TriggeringUserId = @event.UserId,
                        TriggeringUserDisplayName = @event.UserDisplayName,
                        TriggeringUserLogin = @event.UserLogin,
                        RoleLevel = BadgeLevel(@event),
                        Args = args,
                        ReplyParentMessageBody = @event.ReplyParentMessageBody,
                        ReplyParentUserName = @event.ReplyParentUserName,
                        Personality = ctx.Personality,
                        CustomResponseTemplate = ctx.BuiltinResponseOverrides.GetValueOrDefault(
                            commandName
                        ),
                        CancellationToken = cancellationToken,
                    };

                    Result<string> builtinFallbackResult = await builtin.ExecuteAsync(
                        builtinFallbackCtx,
                        cancellationToken
                    );
                    if (
                        builtinFallbackResult.IsSuccess
                        && !string.IsNullOrEmpty(builtinFallbackResult.Value)
                    )
                        await SendResponseAsync(
                            @event,
                            builtinFallbackResult.Value,
                            cancellationToken
                        );

                    await PublishExecutedAsync(
                        @event,
                        command.Name,
                        builtinFallbackResult.IsSuccess,
                        cancellationToken
                    );
                    return;
                }

                // Build template context
                Dictionary<string, string> variables = BuildInitialVariables(@event, args);
                string resolved = await _templateResolver.ResolveAsync(
                    response,
                    variables,
                    @event.BroadcasterId,
                    cancellationToken
                );

                // IChatProvider takes the tenant Guid and resolves it to the Twitch channel string id
                // internally (the invariant boundary lives in HelixChatProvider).
                await SendResponseAsync(@event, resolved, cancellationToken);

                await PublishExecutedAsync(@event, command.Name, true, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error executing command {Command} for {User} in {Channel}",
                commandName,
                @event.UserDisplayName,
                @event.BroadcasterId
            );
            await PublishExecutedAsync(@event, command.Name, false, cancellationToken);
        }
    }

    /// <summary>
    /// Sends a command / built-in RESPONSE back to the caller as a native reply threaded under their triggering
    /// message (Twitch reply), rather than a separate message that @-mentions them — the reply header already
    /// names the recipient, so built-ins no longer prefix "@user". Falls back to a plain send only when there is
    /// no parent message id to reply to (e.g. a non-Twitch source that doesn't carry one).
    /// </summary>
    private async Task SendResponseAsync(
        ChatMessageReceivedEvent @event,
        string text,
        CancellationToken ct
    )
    {
        if (string.IsNullOrEmpty(@event.MessageId))
            await _chat.SendMessageAsync(@event.BroadcasterId, text, ct);
        else
            await _chat.SendReplyAsync(@event.BroadcasterId, @event.MessageId, text, ct);
    }

    /// <summary>
    /// Publishes the single command-execution fact (<see cref="CommandExecutedEvent"/>) the hub broadcast,
    /// the use-count, and the analytics projections all fold from. A bus failure is logged and swallowed —
    /// bookkeeping must never break the chat hot path.
    /// </summary>
    private async Task PublishExecutedAsync(
        ChatMessageReceivedEvent @event,
        string commandName,
        bool succeeded,
        CancellationToken ct
    )
    {
        try
        {
            await _eventBus.PublishAsync(
                new CommandExecutedEvent
                {
                    BroadcasterId = @event.BroadcasterId,
                    CommandName = commandName,
                    UserId = @event.UserId,
                    Username = @event.UserLogin,
                    UserDisplayName = @event.UserDisplayName,
                    Succeeded = succeeded,
                },
                ct
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to publish CommandExecutedEvent for {Command} in {Channel}",
                commandName,
                @event.BroadcasterId
            );
        }
    }

    /// <summary>
    /// True when the channel has explicitly disabled this builtin (<c>ChannelBuiltinCommand.IsEnabled ==
    /// false</c>, mirrored into <see cref="ChannelContext.DisabledBuiltins"/> by <c>ChannelRegistry</c>). A
    /// disabled builtin is silently ignored — exactly like an unrecognized command — rather than executed.
    /// </summary>
    private static bool IsBuiltinDisabled(ChannelContext ctx, string commandName) =>
        ctx.DisabledBuiltins.ContainsKey(commandName);

    /// <summary>
    /// The chat command gate on the unified ladder (roles-permissions §0): effective level =
    /// MAX(live Twitch-badge level, <see cref="IRoleResolver"/> resolved level — community standing,
    /// bot-granted <c>ChannelMemberships</c>, active <c>PermitGrants</c>). The live badge stays in the MAX
    /// because it is the freshest Twitch truth (stored standing rows can lag sync). Hot-path short-circuit:
    /// when the badge level alone meets the floor (always true for Everyone-floor commands and plain
    /// badge-qualified callers), the DB is never touched — the resolver runs only when the badge is
    /// insufficient, i.e. exactly the case where a badge-less Editor membership or a <c>!permit</c>
    /// elevation must be honored instead of silently ignored.
    /// </summary>
    private async Task<bool> HasPermissionAsync(
        ChatMessageReceivedEvent @event,
        int minPermissionLevel,
        CancellationToken ct
    )
    {
        PermissionLevel badge = ChatRole.Resolve(
            @event.IsBroadcaster,
            @event.IsModerator,
            @event.IsVip,
            @event.IsSubscriber,
            @event.Badges
        );
        if (badge.ToLevelValue() >= minPermissionLevel)
            return true;

        // badge < floor here, so MAX(badge, resolved) >= floor reduces to resolved >= floor.
        int? resolved = await TryResolveEffectiveLevelAsync(@event, ct);
        return resolved is int level && level >= minPermissionLevel;
    }

    /// <summary>
    /// The resolver leg of the ladder (community standing, bot-granted memberships, active permits) —
    /// null when the chatter/resolver cannot resolve, so every caller fails CLOSED to the badge level
    /// (a resolver error must never elevate).
    /// </summary>
    private async Task<int?> TryResolveEffectiveLevelAsync(
        ChatMessageReceivedEvent @event,
        CancellationToken ct
    )
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();

            // The event carries the platform user id; the resolver needs the internal User id. A chatter IS
            // a (possibly not-set-up) User row — the same get-or-create seam every chat-ingest handler uses.
            IUserService users = scope.ServiceProvider.GetRequiredService<IUserService>();
            Result<UserDto> user = await users.GetOrCreateAsync(
                @event.UserId,
                @event.UserLogin,
                @event.UserDisplayName,
                @event.Provider,
                ct
            );
            if (user.IsFailure || !Guid.TryParse(user.Value.Id, out Guid viewerUserId))
                return null;

            IRoleResolver roleResolver = scope.ServiceProvider.GetRequiredService<IRoleResolver>();
            Result<int> resolved = await roleResolver.ResolveEffectiveLevelAsync(
                viewerUserId,
                @event.BroadcasterId,
                ct
            );
            return resolved.IsSuccess ? resolved.Value : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Effective-level resolution failed for {User} in {Channel}; falling back to badge level",
                @event.UserLogin,
                @event.BroadcasterId
            );
            return null;
        }
    }

    /// <summary>
    /// The EFFECTIVE role token for the pipeline's <c>user.role</c> variable (item 24c): a badge-less
    /// Editor, a bot-granted membership, or an active <c>!permit</c> elevation must clear a
    /// <c>user_role</c> condition exactly like they clear the command gate — the badge alone lies about
    /// them. Short-circuits on a broadcaster badge (nothing outranks it); degrades to the badge role
    /// when the resolver cannot answer.
    /// </summary>
    private async Task<string> ResolveEffectiveRoleTokenAsync(
        ChatMessageReceivedEvent @event,
        CancellationToken ct
    )
    {
        PermissionLevel badge = ChatRole.Resolve(
            @event.IsBroadcaster,
            @event.IsModerator,
            @event.IsVip,
            @event.IsSubscriber,
            @event.Badges
        );
        if (badge == PermissionLevel.Broadcaster)
            return ChatRole.ToToken(badge);

        int badgeLevel = badge.ToLevelValue();
        int? resolved = await TryResolveEffectiveLevelAsync(@event, ct);
        int effective = Math.Max(badgeLevel, resolved ?? badgeLevel);
        return ChatRole.ToToken(AuthorizationLadder.FromLevelValue(effective));
    }

    private static string PickResponse(string[] responses)
    {
        if (responses.Length == 0)
            return string.Empty;
        if (responses.Length == 1)
            return responses[0];
        return responses[Random.Shared.Next(responses.Length)];
    }

    private static Dictionary<string, string> BuildInitialVariables(
        ChatMessageReceivedEvent @event,
        string args
    )
    {
        string[] argParts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string target = argParts.Length > 0 ? argParts[0].TrimStart('@') : string.Empty;

        Dictionary<string, string> vars = new(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = @event.UserDisplayName,
            ["user.id"] = @event.UserId,
            ["user.name"] = @event.UserLogin,
            // Which platform user.id belongs to — lets the template layer resolve the viewer
            // identity-correctly for non-Twitch chatters ({viewer.data.*}, {viewer.*} stats).
            ["user.provider"] = @event.Provider,
            ["user.role"] = GetUserRole(@event),
            ["target"] = target,
            ["args"] = args,
            ["args.count"] = argParts.Length.ToString(),
        };

        for (int i = 0; i < argParts.Length; i++)
            vars[$"args.{i}"] = argParts[i];

        return vars;
    }

    private static string GetUserRole(ChatMessageReceivedEvent @event) =>
        ChatRole.ToToken(
            ChatRole.Resolve(
                @event.IsBroadcaster,
                @event.IsModerator,
                @event.IsVip,
                @event.IsSubscriber,
                @event.Badges
            )
        );

    /// <summary>The caller's live badge level — what builtins with a standing floor receive.</summary>
    private static int BadgeLevel(ChatMessageReceivedEvent @event) =>
        ChatRole
            .Resolve(
                @event.IsBroadcaster,
                @event.IsModerator,
                @event.IsVip,
                @event.IsSubscriber,
                @event.Badges
            )
            .ToLevelValue();

    // Called at most once per channel per process lifetime (or after an eviction window).
    // Looks up the channel name from DB so the registry context is fully populated.
    private async Task<ChannelContext?> EnsureChannelLoadedAsync(
        Guid broadcasterId,
        string twitchBroadcasterId,
        CancellationToken ct
    )
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IApplicationDbContext db =
                scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            Channel? channel = await db
                .Channels.IgnoreQueryFilters()
                .Where(c => c.Id == broadcasterId)
                .FirstOrDefaultAsync(ct);

            if (channel is null)
            {
                _logger.LogWarning(
                    "ChatMessageHandler: channel {BroadcasterId} not found in DB — dropping message",
                    broadcasterId
                );
                return null;
            }

            return await _registry.GetOrCreateAsync(
                broadcasterId,
                twitchBroadcasterId,
                channel.Name,
                ct
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "ChatMessageHandler: failed to lazy-load channel {BroadcasterId}",
                broadcasterId
            );
            return null;
        }
    }
}

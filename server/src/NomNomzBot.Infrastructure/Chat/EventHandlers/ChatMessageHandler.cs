// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.RateLimiting;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Community.Services;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Games;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Commands.Events;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Games;

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
    private readonly IInboundOriginChatSender _chat;
    private readonly IPipelineEngine _pipeline;
    private readonly IBuiltinCommandCatalog _builtins;
    private readonly ITemplateResolver _templateResolver;
    private readonly IEventBus _eventBus;
    private readonly LiveGameSessionRegistry _gameSessions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ChatMessageHandler> _logger;

    public ChatMessageHandler(
        IChannelRegistry registry,
        IServiceScopeFactory scopeFactory,
        ICooldownManager cooldowns,
        IInboundOriginChatSender chat,
        IPipelineEngine pipeline,
        IBuiltinCommandCatalog builtins,
        ITemplateResolver templateResolver,
        IEventBus eventBus,
        LiveGameSessionRegistry gameSessions,
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
        _gameSessions = gameSessions;
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

        // Registry/context bootstrap happens on the FIRST message of ANY kind on EVERY ingest — plain
        // chat or a command, Twitch/Kick/YouTube alike — never only when the message happens to parse as
        // a `!command`. A channel whose only live platform is non-Twitch (or a chatter who never types a
        // command) must still get welcome/triggers/timers wired up. EnsureChannelLoadedAsync only needs
        // the tenant Guid + the platform broadcaster id carried on the event, so this is provider-agnostic;
        // GetOrCreateAsync (called underneath) is idempotent, so a warm registry short-circuits here with
        // no re-load and no double-fire below.
        ChannelContext? channelCtx = _registry.Get(@event.BroadcasterId);
        if (channelCtx is null)
        {
            channelCtx = await EnsureChannelLoadedAsync(
                @event.BroadcasterId,
                @event.TwitchBroadcasterId,
                cancellationToken
            );
        }

        // Bot-side standing (J.12): a muted/shadowbanned user's chat still displays, persists, and counts
        // toward room activity/{chatters} (it IS visible chat) — but every bot feature ignores them:
        // no welcome, no commands, no triggers, no poll votes.
        bool featureIgnored =
            channelCtx?.ModerationStandingFor(@event.Provider, @event.UserId) is not null;

        if (channelCtx is not null)
        {
            channelCtx.MessageCount++;

            // Track EVERY chatter here (not just command users) so {chatters} reflects the real room —
            // and the first line a user types THIS stream fires the session-first-message trigger
            // (the "welcome them in" chain: sound / overlay / chat, whatever the operator bound).
            bool firstOfSession = channelCtx.SessionChatters.TryAdd(
                @event.UserId,
                @event.UserDisplayName
            );
            if (firstOfSession && channelCtx.IsLive && !featureIgnored)
                await FireSessionFirstMessageAsync(@event, cancellationToken);
        }

        if (featureIgnored)
            return;

        string? text = @event.Message?.Trim();
        if (string.IsNullOrEmpty(text))
            return;

        // The command prefix is per-channel (Channel.CommandPrefix, default "!"), read from the registry's
        // cached settings so this stays a no-DB hot path; a changed prefix applies once the registry reloads.
        string commandPrefix = channelCtx?.CommandPrefix ?? "!";

        bool defaultPrefixed = text.StartsWith(commandPrefix, StringComparison.Ordinal);

        // The context is already ensured above (bootstrap runs before the welcome/trigger checks now),
        // so no second lazy-load is needed here for a command's own PrefixMode/MatchMode resolution.
        ChannelContext? ctx = channelCtx;

        (CachedCommand? resolvedCommand, string resolvedArgs) = ctx is not null
            ? ResolveAuthoredCommand(ctx, text, commandPrefix)
            : (null, string.Empty);

        if (resolvedCommand is null && !defaultPrefixed)
        {
            // Open chat poll: a bare option number is a VOTE and is consumed — it never doubles as a
            // trigger match while the poll runs.
            if (
                channelCtx?.ActiveChatPoll is { } poll
                && int.TryParse(text, out int optionIndex)
                && optionIndex >= 1
                && optionIndex <= poll.OptionCount
            )
            {
                await RecordPollVoteAsync(poll, @event, optionIndex, cancellationToken);
                return;
            }

            // Soundboard trigger surface: a bare, prefix-less word equal to a clip's TriggerWord plays that clip
            // (case-insensitive whole-message match). An exact match CLAIMS the line — a played, refused, or
            // cooling-down soundboard word never also fires a keyword chat trigger — so the two never double-react.
            if (
                channelCtx is not null
                && !channelCtx.SoundTriggers.IsEmpty
                && channelCtx.SoundTriggers.TryGetValue(text, out CachedSoundTrigger? soundTrigger)
            )
            {
                await FireSoundTriggerAsync(soundTrigger, @event, cancellationToken);
                return;
            }

            // Ordinary chat line — the keyword chat-trigger surface ("someone says X → the bot reacts").
            if (channelCtx is not null && !channelCtx.ChatTriggers.IsEmpty)
                await FireChatTriggersAsync(channelCtx, @event, text, cancellationToken);
            return;
        }

        // Live-game precedence (live-games.md D6): while a round is in its Lobby/Running phase, that game
        // OWNS its input keywords. If this message's first token IS an active session's keyword (e.g. !heist
        // typed mid-heist), it means JOIN the round — not run a same-named authored command. The event fans
        // out independently to LiveGameInputListener, which is the authoritative consumer for that message,
        // so the command path stands down here to avoid a double-fire. This is a read-only lookup against the
        // same singleton registry the listener uses (no cross-handler "handled" flag).
        if (IsClaimedByActiveGame(@event.BroadcasterId, text))
            return;

        string commandName;
        string args;
        if (resolvedCommand is not null)
        {
            commandName = resolvedCommand.Name.TrimStart('!').ToLowerInvariant();
            args = resolvedArgs;
        }
        else
        {
            // No per-command trigger matched (Custom prefix / non-default MatchMode) — fall back to the
            // classic default-prefix parse so built-ins (which are always Default/StartsWith) still resolve.
            string afterPrefix = text[commandPrefix.Length..];
            int spaceIdx = afterPrefix.IndexOf(' ');
            commandName = (spaceIdx > 0 ? afterPrefix[..spaceIdx] : afterPrefix).ToLowerInvariant();
            args = spaceIdx > 0 ? afterPrefix[(spaceIdx + 1)..].Trim() : string.Empty;
        }

        if (string.IsNullOrEmpty(commandName))
            return;

        if (ctx is null)
            return;

        ctx.LastActivityAt = _timeProvider.GetUtcNow();

        // Reserved built-ins (the data-subject rights floor, gdpr-crypto.md §9) resolve BEFORE any
        // authored command: a channel command can never shadow them and a channel toggle never
        // disables them — the rights floor is always-on.
        IBuiltinCommand? builtin = _builtins.Get(commandName);
        bool isReserved = builtin is { IsReserved: true };

        // ResolveAuthoredCommand already evaluated EVERY command's own trigger model (including the
        // Default/StartsWith case), so its result is authoritative — a plain dictionary lookup by
        // parsed name here would bypass Exact/Contains/Regex/Custom-prefix rejections for a same-named
        // command whose match actually failed (e.g. an Exact-mode "!hi" command must NOT also answer to
        // "!hi there" just because "hi" is a key in the map).
        CachedCommand? command = isReserved ? null : resolvedCommand;
        if (isReserved || command is null)
        {
            // Fall back to built-in catalog (code-defined commands like !uptime).
            if (builtin is null)
                return;

            if (!isReserved && IsBuiltinDisabled(ctx, commandName))
                return;

            if (
                !await HasPermissionAsync(
                    @event,
                    builtin.DefaultMinPermissionLevel,
                    cancellationToken
                )
            )
            {
                await SendPermissionDeniedNoticeAsync(@event, cancellationToken);
                return;
            }

            if (_cooldowns.IsOnCooldown(cooldownChannelKey, commandName, IsCooldownExempt(@event)))
            {
                await SendCooldownNoticeAsync(@event, cancellationToken);
                return;
            }

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

            BuiltinOutcome builtinOutcome = await ExecuteBuiltinAndSendAsync(
                builtin,
                builtinCtx,
                @event,
                cancellationToken
            );

            if (builtinOutcome == BuiltinOutcome.SendFailed)
                await SendBuiltinFailureNoticeAsync(@event, cancellationToken);

            await PublishExecutedAsync(
                @event,
                commandName,
                builtinOutcome == BuiltinOutcome.Success,
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
            await SendPermissionDeniedNoticeAsync(@event, cancellationToken);
            return;
        }

        // Global cooldown check — broadcaster/moderator are exempt (never held by a command cooldown).
        bool cooldownExempt = IsCooldownExempt(@event);
        if (
            command.GlobalCooldown > 0
            && _cooldowns.IsOnCooldown(cooldownChannelKey, commandName, cooldownExempt)
        )
        {
            _logger.LogDebug(
                "Command {Command} on global cooldown in {Channel}",
                commandName,
                @event.BroadcasterId
            );
            await SendCooldownNoticeAsync(@event, cancellationToken);
            return;
        }

        // Per-user cooldown check
        if (
            command.UserCooldown > 0
            && _cooldowns.IsOnCooldown(
                cooldownChannelKey,
                commandName,
                cooldownExempt,
                @event.UserId
            )
        )
        {
            _logger.LogDebug(
                "Command {Command} on user cooldown for {User} in {Channel}",
                commandName,
                @event.UserDisplayName,
                @event.BroadcasterId
            );
            await SendCooldownNoticeAsync(@event, cancellationToken);
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
                // PartiallyFailed (a step broke the run early) is the one outcome the invoker was never
                // told about before — the pipeline may have sent nothing to chat before it broke, so send
                // exactly one failure notice here instead of leaving the caller guessing.
                bool pipelineSucceeded =
                    pipelineResult.Outcome is PipelineOutcome.Completed or PipelineOutcome.Stopped;
                if (!pipelineSucceeded)
                    await SendPipelineFailureNoticeAsync(@event, cancellationToken);

                await PublishExecutedAsync(
                    @event,
                    command.Name,
                    pipelineSucceeded,
                    cancellationToken
                );
            }
            else
            {
                // Simple response command — pick a response (round-robin or random).
                // If the command has no template responses (misconfigured or a builtin key that
                // lives in the Commands table for metadata purposes), fall through to the builtin
                // catalog so the code-defined handler still fires (e.g. !sr, !song, !uptime).
                string response = PickResponse(
                    command.TemplateResponses,
                    $"{ctx.BroadcasterId}:{command.Name}"
                );
                if (string.IsNullOrEmpty(response))
                {
                    // Reuses the catalog lookup done up front (reserved built-ins never reach here —
                    // they short-circuit before the authored-command path).
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

                    BuiltinOutcome builtinFallbackOutcome = await ExecuteBuiltinAndSendAsync(
                        builtin,
                        builtinFallbackCtx,
                        @event,
                        cancellationToken
                    );

                    if (builtinFallbackOutcome == BuiltinOutcome.SendFailed)
                        await SendBuiltinFailureNoticeAsync(@event, cancellationToken);

                    await PublishExecutedAsync(
                        @event,
                        command.Name,
                        builtinFallbackOutcome == BuiltinOutcome.Success,
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
                // internally (the invariant boundary lives in HelixChatProvider). Before this, the bool
                // was discarded and success hardcoded, so a template response that never reached chat
                // (transport rejection) still recorded as a successful CommandExecutedEvent — the same
                // defect class S008 fixed for pipelines and S008c fixed for builtins.
                bool templateSent = await SendResponseAsync(@event, resolved, cancellationToken);
                if (!templateSent)
                    await SendBuiltinFailureNoticeAsync(@event, cancellationToken);

                await PublishExecutedAsync(@event, command.Name, templateSent, cancellationToken);
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
    /// Resolves the first authored command whose own trigger model (commands-pipelines.md §3.2.1:
    /// <c>PrefixMode</c>/<c>CustomPrefix</c>/<c>MatchMode</c>/<c>MatchPattern</c>) matches the raw message —
    /// evaluated against EACH command's own effective prefix, not just the channel default, so a Custom-prefix
    /// or non-StartsWith command fires from its own trigger and never from the channel's default prefix.
    /// </summary>
    private static (CachedCommand? Command, string Args) ResolveAuthoredCommand(
        ChannelContext ctx,
        string text,
        string channelPrefix
    )
    {
        // ctx.Commands stores the SAME CachedCommand instance under its name key AND every alias key, so
        // iterating .Values visits a multi-alias command more than once — deduplicate by reference and, for
        // each, test every name it answers to (canonical name + aliases) against its own trigger model.
        HashSet<CachedCommand> visited = new(ReferenceEqualityComparer.Instance);
        foreach (CachedCommand candidate in ctx.Commands.Values)
        {
            if (!visited.Add(candidate))
                continue;

            string effectivePrefix = candidate.PrefixMode switch
            {
                "Custom" => candidate.CustomPrefix ?? string.Empty,
                "None" => string.Empty,
                _ => channelPrefix,
            };

            foreach (string name in NamesOf(candidate))
            {
                string trigger = effectivePrefix + name;

                switch (candidate.MatchMode)
                {
                    case "Exact":
                        if (text.Equals(trigger, StringComparison.OrdinalIgnoreCase))
                            return (candidate, string.Empty);
                        break;

                    case "Contains":
                        if (ContainsWholeWord(text, trigger))
                            return (candidate, string.Empty);
                        break;

                    case "Regex":
                        if (candidate.CompiledRegex?.IsMatch(text) == true)
                            return (candidate, string.Empty);
                        break;

                    default: // StartsWith
                        if (
                            text.StartsWith(trigger, StringComparison.OrdinalIgnoreCase)
                            && (
                                text.Length == trigger.Length
                                || char.IsWhiteSpace(text[trigger.Length])
                            )
                        )
                        {
                            string args =
                                text.Length > trigger.Length
                                    ? text[(trigger.Length + 1)..].Trim()
                                    : string.Empty;
                            return (candidate, args);
                        }
                        break;
                }

                // Regex has one fixed pattern — trying it once per alias would just re-run the same match.
                if (candidate.MatchMode == "Regex")
                    break;
            }
        }

        return (null, string.Empty);
    }

    private static IEnumerable<string> NamesOf(CachedCommand candidate)
    {
        yield return candidate.Name;
        foreach (string alias in candidate.Aliases)
            yield return alias;
    }

    /// <summary>Whole-word, case-insensitive substring match — used by <c>MatchMode=Contains</c> so a trigger
    /// embedded inside a longer word (e.g. "cat" inside "category") never fires.</summary>
    private static bool ContainsWholeWord(string text, string word)
    {
        if (word.Length == 0)
            return false;

        int idx = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        while (idx >= 0)
        {
            bool leftBoundary = idx == 0 || char.IsWhiteSpace(text[idx - 1]);
            bool rightBoundary =
                idx + word.Length == text.Length || char.IsWhiteSpace(text[idx + word.Length]);
            if (leftBoundary && rightBoundary)
                return true;

            idx = text.IndexOf(word, idx + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// True when an ACTIVE live-game round for this channel claims the message's first token as one of its
    /// input keywords — the guard that lets a live round shadow a same-named authored command (typing
    /// <c>!heist</c> mid-heist JOINS the heist rather than running a <c>!heist</c> command). Mirrors
    /// <c>LiveGameInputListener</c>'s hot-path gate exactly (non-terminal session, Lobby/Running phase,
    /// case-insensitive first-token keyword match) against the shared singleton
    /// <see cref="LiveGameSessionRegistry"/>, so the two handlers agree on ownership. Purely read-only: a
    /// lock-free registry lookup on the miss path, so chat stays cheap while no game runs.
    /// </summary>
    private bool IsClaimedByActiveGame(Guid broadcasterId, string text)
    {
        if (
            !_gameSessions.TryGet(broadcasterId, out LiveGameSessionRuntime? runtime)
            || runtime.Terminal
            || runtime.Phase is not (LiveGamePhase.Lobby or LiveGamePhase.Running)
        )
            return false;

        string first = text.Split(' ', 2)[0];
        return runtime.Game.Manifest.InputKeywords.Any(k =>
            string.Equals(k, first, StringComparison.OrdinalIgnoreCase)
        );
    }

    /// <summary>
    /// Sends a command / built-in RESPONSE back to the caller as a native reply threaded under their triggering
    /// message (Twitch reply), rather than a separate message that @-mentions them — the reply header already
    /// names the recipient, so built-ins no longer prefix "@user". Falls back to a plain send only when there is
    /// no parent message id to reply to (e.g. a non-Twitch source that doesn't carry one), OR when the reply
    /// form itself was rejected (e.g. a deleted/invalid parent message) — in that case the fallback still
    /// addresses the user via an inline mention, since the reply header is no longer there to do it. Returns the
    /// REAL send outcome — a failed transport send is never reported to the caller as delivered.
    /// </summary>
    private async Task<bool> SendResponseAsync(
        ChatMessageReceivedEvent @event,
        string text,
        CancellationToken ct
    )
    {
        if (string.IsNullOrEmpty(@event.MessageId))
            return (
                await _chat.SendMessageAsync(@event.BroadcasterId, @event.Provider, text, ct)
            ).IsSuccess;

        if (
            (
                await _chat.SendReplyAsync(
                    @event.BroadcasterId,
                    @event.Provider,
                    @event.MessageId,
                    text,
                    ct
                )
            ).IsSuccess
        )
            return true;

        return (
            await _chat.SendMessageAsync(
                @event.BroadcasterId,
                @event.Provider,
                $"@{@event.UserDisplayName} {text}",
                ct
            )
        ).IsSuccess;
    }

    /// <summary>
    /// The single fixed notice sent to the invoker when a gate silently blocked their command before —
    /// cooldown and permission-denied used to return with no chat line at all, leaving the caller guessing
    /// whether the bot even saw the message. Exactly ONE line per gated invocation, never zero, never both.
    /// </summary>
    private Task SendCooldownNoticeAsync(ChatMessageReceivedEvent @event, CancellationToken ct) =>
        SendResponseAsync(@event, "That command is still on cooldown.", ct);

    private Task SendPermissionDeniedNoticeAsync(
        ChatMessageReceivedEvent @event,
        CancellationToken ct
    ) => SendResponseAsync(@event, "You don't have permission to use that command.", ct);

    /// <summary>
    /// The single fixed notice sent to the invoker when a pipeline-backed command run PartiallyFailed
    /// (a step broke the run early) — without this the invoker has no way to know their command hit a snag,
    /// since a partially-run pipeline may have sent nothing to chat itself before failing.
    /// </summary>
    private Task SendPipelineFailureNoticeAsync(
        ChatMessageReceivedEvent @event,
        CancellationToken ct
    ) => SendResponseAsync(@event, "Sorry, that command hit a snag and didn't finish.", ct);

    /// <summary>
    /// The single fixed notice sent to the invoker when a builtin's reply never actually reached chat
    /// (<see cref="BuiltinOutcome.SendFailed"/>) — mirrors <see cref="SendPipelineFailureNoticeAsync"/> so a
    /// builtin whose transport send failed is never left silent even though its logic ran fine.
    /// </summary>
    private Task SendBuiltinFailureNoticeAsync(
        ChatMessageReceivedEvent @event,
        CancellationToken ct
    ) => SendResponseAsync(@event, "Sorry, that command hit a snag and didn't finish.", ct);

    /// <summary>
    /// Runs a builtin and, when it produced a reply, sends it — returning the REAL outcome so the caller
    /// (direct dispatch or the template-response fallback) can record an honest <see cref="CommandExecutedEvent"/>
    /// and, on <see cref="BuiltinOutcome.SendFailed"/>, give the invoker exactly one failure line. Before this,
    /// both builtin call sites discarded the chat-send bool from <see cref="SendResponseAsync"/> and always
    /// recorded success as long as the builtin's own logic didn't fail — a reply that never reached chat was
    /// still reported as delivered.
    /// </summary>
    private async Task<BuiltinOutcome> ExecuteBuiltinAndSendAsync(
        IBuiltinCommand builtin,
        BuiltinCommandContext builtinCtx,
        ChatMessageReceivedEvent @event,
        CancellationToken ct
    )
    {
        Result<string> result = await builtin.ExecuteAsync(builtinCtx, ct);
        if (!result.IsSuccess)
            return BuiltinOutcome.ExecutionFailed;

        if (string.IsNullOrEmpty(result.Value))
            return BuiltinOutcome.Success;

        bool sent = await SendResponseAsync(@event, result.Value, ct);
        return sent ? BuiltinOutcome.Success : BuiltinOutcome.SendFailed;
    }

    /// <summary>
    /// Real outcome of a builtin invocation, distinguishing a builtin whose OWN logic failed
    /// (<see cref="ExecutionFailed"/>) from one that ran fine but whose reply never reached chat
    /// (<see cref="SendFailed"/>) — both must record as a failed <see cref="CommandExecutedEvent"/>, but only
    /// <see cref="SendFailed"/> needs a failure notice (an <see cref="ExecutionFailed"/> builtin already chose
    /// to say nothing).
    /// </summary>
    private enum BuiltinOutcome
    {
        Success,
        ExecutionFailed,
        SendFailed,
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
        return resolved is { } level && level >= minPermissionLevel;
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

    /// <summary>
    /// The line a random-response command speaks, never the same one twice running. A uniform draw repeats
    /// back-to-back 1-in-N of the time — with a 20-line pool that is every twentieth use, and in chat an
    /// immediately repeated "random" line reads as the bot being broken rather than as chance. Excluding
    /// only the PREVIOUS line keeps every other line equally likely, so the pool still feels random; it does
    /// not cycle or exhaust.
    /// </summary>
    private string PickResponse(string[] responses, string commandKey)
    {
        if (responses.Length == 0)
            return string.Empty;
        if (responses.Length == 1)
            return responses[0];

        _lastResponseIndex.TryGetValue(commandKey, out int previous);
        int index = Random.Shared.Next(responses.Length - 1);
        // Map the drawn index around the previous one, so the previous line is the only one excluded and
        // the remaining N-1 stay uniformly likely.
        if (index >= previous)
            index++;
        _lastResponseIndex[commandKey] = index;
        return responses[index];
    }

    /// <summary>The last line each command spoke, so the next draw can avoid repeating it. Keyed by
    /// channel+command, bounded by the number of authored commands, and purely cosmetic — losing it on a
    /// restart costs nothing more than one possible repeat.</summary>
    private readonly ConcurrentDictionary<string, int> _lastResponseIndex = new();

    /// <summary>
    /// Matches the channel's cached keyword triggers against an ordinary chat line: role floor first,
    /// then the pattern, then the per-trigger channel cooldown (the spam guard) — FIRST match wins, so
    /// one line never fires a barrage. A matched trigger runs its bound pipeline (the full reaction
    /// chain) or sends its resolved template line. Failures never reach the chat hot path.
    /// </summary>
    private async Task FireChatTriggersAsync(
        ChannelContext ctx,
        ChatMessageReceivedEvent @event,
        string text,
        CancellationToken ct
    )
    {
        int speakerLevel = BadgeLevel(@event);
        string cooldownChannelKey = @event.BroadcasterId.ToString();

        foreach (CachedChatTrigger trigger in ctx.ChatTriggers.Values)
        {
            if (speakerLevel < trigger.MinPermissionLevel)
                continue;
            if (!TriggerMatches(trigger, text))
                continue;

            string cooldownKey = $"trigger:{trigger.Id:N}";
            if (_cooldowns.IsOnCooldown(cooldownChannelKey, cooldownKey, IsCooldownExempt(@event)))
                return; // matched but cooling down — first match still wins, silently.

            if (trigger.CooldownSeconds > 0)
                _cooldowns.SetCooldown(
                    cooldownChannelKey,
                    cooldownKey,
                    TimeSpan.FromSeconds(trigger.CooldownSeconds)
                );

            try
            {
                await ExecuteChatTriggerAsync(trigger, @event, text, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Chat trigger {TriggerId} failed in {Channel}",
                    trigger.Id,
                    @event.BroadcasterId
                );
            }
            return;
        }
    }

    /// <summary>
    /// Plays a soundboard clip whose <see cref="CachedSoundTrigger.TriggerWord"/> the chatter typed: the
    /// community-standing floor first, then the per-clip cooldown (the spam guard), then resolve-and-push to the
    /// overlay audio bus (the same path <c>play_sound</c> and the dashboard preview use — resolved through a scope
    /// because the sound services are scoped). A below-floor speaker is silently refused; a clip that resolves as
    /// missing/disabled simply does not play. Failures never reach the chat hot path.
    /// </summary>
    private async Task FireSoundTriggerAsync(
        CachedSoundTrigger trigger,
        ChatMessageReceivedEvent @event,
        CancellationToken ct
    )
    {
        if (!await HasPermissionAsync(@event, trigger.MinPermissionLevel, ct))
            return;

        string cooldownChannelKey = @event.BroadcasterId.ToString();
        string cooldownKey = $"sound:{trigger.ClipId:N}";
        if (_cooldowns.IsOnCooldown(cooldownChannelKey, cooldownKey, IsCooldownExempt(@event)))
            return;

        if (trigger.CooldownSeconds > 0)
            _cooldowns.SetCooldown(
                cooldownChannelKey,
                cooldownKey,
                TimeSpan.FromSeconds(trigger.CooldownSeconds)
            );

        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISoundClipService clips = scope.ServiceProvider.GetRequiredService<ISoundClipService>();
            ISoundClipOverlayNotifier overlay =
                scope.ServiceProvider.GetRequiredService<ISoundClipOverlayNotifier>();

            Result<SoundPlaybackDto> resolved = await clips.ResolveForPlaybackAsync(
                @event.BroadcasterId,
                trigger.ClipId.ToString(),
                volumeOverride: null,
                ct
            );
            if (!resolved.IsSuccess)
                return;

            await overlay.PlaySoundAsync(@event.BroadcasterId, resolved.Value, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Sound trigger {ClipId} failed in {Channel}",
                trigger.ClipId,
                @event.BroadcasterId
            );
        }
    }

    /// <summary>
    /// Records (or changes) the speaker's vote in the open chat poll — one CURRENT vote per viewer per
    /// poll, last vote wins. Failures never reach the chat hot path.
    /// </summary>
    private async Task RecordPollVoteAsync(
        CachedChatPoll poll,
        ChatMessageReceivedEvent @event,
        int optionIndex,
        CancellationToken ct
    )
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IChatPollService polls = scope.ServiceProvider.GetRequiredService<IChatPollService>();
            await polls.RecordVoteAsync(
                @event.BroadcasterId,
                poll.Id,
                @event.Provider,
                @event.UserId,
                optionIndex,
                ct
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Chat poll vote failed for poll {PollId} in {Channel}",
                poll.Id,
                @event.BroadcasterId
            );
        }
    }

    private static bool TriggerMatches(CachedChatTrigger trigger, string text)
    {
        StringComparison comparison = trigger.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        try
        {
            return trigger.MatchType switch
            {
                Domain.Commands.Entities.ChatTriggerMatchType.Exact => text.Equals(
                    trigger.Pattern,
                    comparison
                ),
                Domain.Commands.Entities.ChatTriggerMatchType.StartsWith => text.StartsWith(
                    trigger.Pattern,
                    comparison
                ),
                Domain.Commands.Entities.ChatTriggerMatchType.Regex =>
                    trigger.CompiledRegex?.IsMatch(text) == true,
                _ => text.Contains(trigger.Pattern, comparison),
            };
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            // A pathological pattern burned its 100ms budget — treat as no match, never stall chat.
            return false;
        }
    }

    private async Task ExecuteChatTriggerAsync(
        CachedChatTrigger trigger,
        ChatMessageReceivedEvent @event,
        string text,
        CancellationToken ct
    )
    {
        Dictionary<string, string> variables = BuildInitialVariables(@event, text);

        if (!string.IsNullOrWhiteSpace(trigger.PipelineGraphJson))
        {
            await _pipeline.ExecuteAsync(
                new()
                {
                    BroadcasterId = @event.BroadcasterId,
                    PipelineJson = trigger.PipelineGraphJson,
                    TriggeredByUserId = @event.UserId,
                    TriggeredByDisplayName = @event.UserDisplayName,
                    MessageId = @event.MessageId,
                    RawMessage = @event.Message ?? string.Empty,
                    InitialVariables = variables,
                },
                ct
            );
            return;
        }

        if (string.IsNullOrWhiteSpace(trigger.Response))
            return;

        string resolved = await _templateResolver.ResolveAsync(
            trigger.Response,
            variables,
            @event.BroadcasterId,
            ct
        );
        if (!string.IsNullOrWhiteSpace(resolved))
            await _chat.SendMessageAsync(@event.BroadcasterId, @event.Provider, resolved, ct);
    }

    /// <summary>
    /// The session-first-message trigger: dispatched through the shared executor the first time a user
    /// speaks during THIS stream (session-deduped via <c>ChannelContext.SessionChatters</c>, which
    /// <c>stream.online</c> clears). Failures never reach the chat hot path.
    /// </summary>
    private async Task FireSessionFirstMessageAsync(
        ChatMessageReceivedEvent @event,
        CancellationToken ct
    )
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IEventResponseExecutor executor =
                scope.ServiceProvider.GetRequiredService<IEventResponseExecutor>();
            await executor.ExecuteAsync(
                @event.BroadcasterId,
                "engagement.session_first_message",
                @event.UserId,
                @event.UserDisplayName,
                new(StringComparer.OrdinalIgnoreCase)
                {
                    ["user"] = @event.UserDisplayName,
                    ["user.id"] = @event.UserId,
                    ["viewer.name"] = @event.UserDisplayName,
                },
                ct
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "session_first_message trigger failed for {Channel}",
                @event.BroadcasterId
            );
        }
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

    /// <summary>
    /// Broadcaster and moderators are trusted operators and are NEVER held by a command cooldown —
    /// cooldowns exist to stop viewer spam. Every command-cooldown gate in this handler routes through
    /// this single predicate, so a new gate inherits the exemption for free instead of re-deriving role.
    /// </summary>
    private static bool IsCooldownExempt(ChatMessageReceivedEvent @event) =>
        @event.IsBroadcaster || @event.IsModerator;

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

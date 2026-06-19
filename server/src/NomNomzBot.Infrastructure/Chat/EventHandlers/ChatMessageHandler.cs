// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.RateLimiting;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Chat.EventHandlers;

/// <summary>
/// Hot-path handler for every incoming chat message.
/// 1. Checks for command prefix (!commandname)
/// 2. Looks up command in the in-memory ChannelRegistry (no DB hit)
/// 3. Validates permission level: broadcaster > mod > vip > sub > viewer
/// 4. Checks global and per-user cooldowns via ICooldownManager
/// 5. For response-type commands: resolves template variables, sends message
/// 6. For pipeline-type commands: delegates to IPipelineEngine
/// 7. Publishes CommandExecutedEvent or CommandFailedEvent
/// </summary>
public sealed class ChatMessageHandler : IEventHandler<ChatMessageReceivedEvent>
{
    private readonly IChannelRegistry _registry;
    private readonly ICooldownManager _cooldowns;
    private readonly IChatProvider _chat;
    private readonly IPipelineEngine _pipeline;
    private readonly ITemplateResolver _templateResolver;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ChatMessageHandler> _logger;

    public ChatMessageHandler(
        IChannelRegistry registry,
        ICooldownManager cooldowns,
        IChatProvider chat,
        IPipelineEngine pipeline,
        ITemplateResolver templateResolver,
        TimeProvider timeProvider,
        ILogger<ChatMessageHandler> logger
    )
    {
        _registry = registry;
        _cooldowns = cooldowns;
        _chat = chat;
        _pipeline = pipeline;
        _templateResolver = templateResolver;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task HandleAsync(
        ChatMessageReceivedEvent @event,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(@event.BroadcasterId))
            return;

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
            return; // channel not registered

        ctx.LastActivityAt = _timeProvider.GetUtcNow();
        ctx.SessionChatters.TryAdd(@event.UserId, @event.UserDisplayName);

        // Look up command in in-memory cache (O(1), no DB hit)
        if (!ctx.Commands.TryGetValue(commandName, out CachedCommand? command))
            return;

        // Permission check
        if (!HasPermission(@event, command.Permission))
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
        if (
            command.GlobalCooldown > 0
            && _cooldowns.IsOnCooldown(@event.BroadcasterId, commandName)
        )
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
            && _cooldowns.IsOnCooldown(@event.BroadcasterId, commandName, @event.UserId)
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
                @event.BroadcasterId,
                commandName,
                TimeSpan.FromSeconds(command.GlobalCooldown)
            );
        if (command.UserCooldown > 0)
            _cooldowns.SetCooldown(
                @event.BroadcasterId,
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
            if (command.Type == "pipeline" && !string.IsNullOrEmpty(command.PipelineJson))
            {
                PipelineRequest request = new()
                {
                    BroadcasterId = @event.BroadcasterId,
                    PipelineJson = command.PipelineJson,
                    TriggeredByUserId = @event.UserId,
                    TriggeredByDisplayName = @event.UserDisplayName,
                    MessageId = @event.MessageId,
                    RawMessage = @event.Message ?? string.Empty,
                    InitialVariables = BuildInitialVariables(@event, args),
                };

                await _pipeline.ExecuteAsync(request, cancellationToken);
            }
            else
            {
                // Simple response command — pick a response (round-robin or random)
                string response = PickResponse(command.Responses);
                if (string.IsNullOrEmpty(response))
                    return;

                // Build template context
                Dictionary<string, string> variables = BuildInitialVariables(@event, args);
                string resolved = await _templateResolver.ResolveAsync(
                    response,
                    variables,
                    @event.BroadcasterId!,
                    cancellationToken
                );

                await _chat.SendMessageAsync(@event.BroadcasterId, resolved, cancellationToken);
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
        }
    }

    private static bool HasPermission(ChatMessageReceivedEvent @event, string requiredPermission)
    {
        return requiredPermission.ToLowerInvariant() switch
        {
            "broadcaster" => @event.IsBroadcaster,
            "moderator" or "mod" => @event.IsBroadcaster || @event.IsModerator,
            "vip" => @event.IsBroadcaster || @event.IsModerator || @event.IsVip,
            "subscriber" or "sub" => @event.IsBroadcaster
                || @event.IsModerator
                || @event.IsVip
                || @event.IsSubscriber,
            "viewer" or "everyone" or "" => true,
            _ => true,
        };
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
            ["user.role"] = GetUserRole(@event),
            ["target"] = target,
            ["args"] = args,
            ["args.count"] = argParts.Length.ToString(),
        };

        for (int i = 0; i < argParts.Length; i++)
            vars[$"args.{i}"] = argParts[i];

        return vars;
    }

    private static string GetUserRole(ChatMessageReceivedEvent @event)
    {
        if (@event.IsBroadcaster)
            return "broadcaster";
        if (@event.IsModerator)
            return "moderator";
        if (@event.IsVip)
            return "vip";
        if (@event.IsSubscriber)
            return "subscriber";
        return "viewer";
    }
}

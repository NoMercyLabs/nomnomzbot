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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Stream.PipelineActions;

/// <summary>
/// Pipeline action that sends a Twitch shoutout via Helix POST /chat/shoutouts.
///
/// Parameters:
///   user_id  — Twitch user ID **or login/channel name** to shout out (required; a leading @ is
///              tolerated, a login is resolved to its id via Helix Get Users). Supports variable
///              substitution — e.g. "{timer.message}" for a rotating auto-shoutout list.
///   cooldown_minutes — Per-user cooldown in minutes (default: 60).
///   global_cooldown_minutes — Global shoutout cooldown in minutes (default: 2).
///
/// Usage example:
///   { "type": "shoutout", "user_id": "{user.id}", "cooldown_minutes": 60 }
/// </summary>
public sealed class ShoutoutAction : ICommandAction
{
    private static readonly TimeSpan DefaultPerUserCooldown = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan DefaultGlobalCooldown = TimeSpan.FromMinutes(2);

    private readonly ITwitchChatApi _chat;
    private readonly ITwitchUsersApi _users;
    private readonly IChannelRegistry _registry;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ShoutoutAction> _logger;

    public string ActionType => "shoutout";

    public ShoutoutAction(
        ITwitchChatApi chat,
        ITwitchUsersApi users,
        IChannelRegistry registry,
        TimeProvider timeProvider,
        ILogger<ShoutoutAction> logger
    )
    {
        _chat = chat;
        _users = users;
        _registry = registry;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string? rawUserId = action.GetString("user_id") ?? string.Empty;

        // Resolve {variable} references inside the user_id param
        if (rawUserId.StartsWith('{') && rawUserId.EndsWith('}'))
        {
            string key = rawUserId[1..^1];
            ctx.Variables.TryGetValue(key, out rawUserId!);
        }

        rawUserId = rawUserId?.Trim().TrimStart('@') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawUserId))
            return ActionResult.Failure("shoutout action requires a non-empty 'user_id'");

        // A curated shoutout list holds channel NAMES; Helix wants the numeric id — resolve a login.
        if (!rawUserId.All(char.IsAsciiDigit))
        {
            Result<IReadOnlyList<TwitchUser>> lookup = await _users.GetUsersByLoginsAsync(
                [rawUserId.ToLowerInvariant()],
                ctx.CancellationToken
            );
            string? resolvedId = lookup.IsSuccess ? lookup.Value.FirstOrDefault()?.Id : null;
            if (string.IsNullOrEmpty(resolvedId))
                return ActionResult.Failure(
                    $"shoutout target '{rawUserId}' was not found on Twitch"
                );
            rawUserId = resolvedId;
        }

        int perUserMinutes = action.GetInt("cooldown_minutes", 60);
        int globalMinutes = action.GetInt("global_cooldown_minutes", 2);
        TimeSpan perUserCooldown = TimeSpan.FromMinutes(perUserMinutes);
        TimeSpan globalCooldown = TimeSpan.FromMinutes(globalMinutes > 0 ? globalMinutes : 2);

        // Check cooldowns via ChannelContext
        ChannelContext? channelCtx = _registry.Get(ctx.BroadcasterId);
        if (channelCtx is not null)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();

            // Global cooldown
            if (
                channelCtx.LastGlobalShoutout.HasValue
                && now - channelCtx.LastGlobalShoutout.Value < globalCooldown
            )
            {
                _logger.LogDebug(
                    "Shoutout to {UserId} skipped — global cooldown active",
                    rawUserId
                );
                return ActionResult.Success("skipped (global cooldown)");
            }

            // Per-user cooldown
            if (
                channelCtx.LastShoutoutPerUser.TryGetValue(rawUserId, out DateTimeOffset lastSo)
                && now - lastSo < perUserCooldown
            )
            {
                _logger.LogDebug(
                    "Shoutout to {UserId} skipped — per-user cooldown active",
                    rawUserId
                );
                return ActionResult.Success("skipped (per-user cooldown)");
            }
        }

        // rawUserId is the Twitch id of the channel to shout out. The sub-client resolves this channel's
        // tenant Guid → Twitch id internally and sends the shoutout as its own moderator.
        Result result = await _chat.SendShoutoutAsync(
            ctx.BroadcasterId,
            rawUserId,
            ctx.CancellationToken
        );
        bool success = result.IsSuccess;

        if (success && channelCtx is not null)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            channelCtx.LastGlobalShoutout = now;
            channelCtx.LastShoutoutPerUser[rawUserId] = now;
        }

        return success
            ? ActionResult.Success($"shoutout sent to {rawUserId}")
            : ActionResult.Failure($"Twitch shoutout API failed for {rawUserId}");
    }
}

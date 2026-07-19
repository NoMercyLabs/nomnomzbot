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

namespace NomNomzBot.Infrastructure.Stream.PipelineActions;

/// <summary>
/// Pipeline action that starts a Twitch raid via Helix POST /raids on the broadcaster's token
/// (<c>channel:manage:raids</c>).
///
/// Parameters:
///   target        — Twitch login/channel name **or numeric user id** to raid (required; a leading @ is
///                   tolerated; supports "{variable}" substitution like shoutout). A login is resolved to
///                   its id via Helix Get Users.
///   delay_seconds — Optional wait before firing, clamped to 0–90 (a hype-building countdown). Honored as an
///                   INTERNAL <see cref="Task.Delay(TimeSpan, CancellationToken)"/> under the pipeline's
///                   cancellation token — not via the engine's wait step — so one block carries the whole
///                   "announce, pause, raid" beat without a separate wait action.
///
/// Typed failures: missing/unknown target, Helix refusal (offline target, missing scope, pending raid).
///
/// Usage example:
///   { "type": "start_raid", "target": "{args.1}", "delay_seconds": 10 }
/// </summary>
public sealed class StartRaidAction : ICommandAction
{
    private const int MaxDelaySeconds = 90;

    private readonly ITwitchRaidsApi _raids;
    private readonly ITwitchUsersApi _users;
    private readonly ILogger<StartRaidAction> _logger;

    public string ActionType => "start_raid";
    public string Category => "stream";
    public string Description => "Start a raid to another channel";

    public StartRaidAction(
        ITwitchRaidsApi raids,
        ITwitchUsersApi users,
        ILogger<StartRaidAction> logger
    )
    {
        _raids = raids;
        _users = users;
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string? rawTarget = action.GetString("target") ?? string.Empty;

        // Resolve {variable} references inside the target param (same convention as shoutout's user_id).
        if (rawTarget.StartsWith('{') && rawTarget.EndsWith('}'))
        {
            string key = rawTarget[1..^1];
            ctx.Variables.TryGetValue(key, out rawTarget!);
        }

        rawTarget = rawTarget?.Trim().TrimStart('@') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawTarget))
            return ActionResult.Failure("start_raid action requires a non-empty 'target'");

        // A raid command names a channel; Helix wants the numeric id — resolve a login.
        string targetId = rawTarget;
        if (!targetId.All(char.IsAsciiDigit))
        {
            Result<IReadOnlyList<TwitchUser>> lookup = await _users.GetUsersByLoginsAsync(
                [targetId.ToLowerInvariant()],
                ctx.CancellationToken
            );
            string? resolvedId = lookup.IsSuccess ? lookup.Value.FirstOrDefault()?.Id : null;
            if (string.IsNullOrEmpty(resolvedId))
                return ActionResult.Failure(
                    $"start_raid target '{rawTarget}' was not found on Twitch"
                );
            targetId = resolvedId;
        }

        int delaySeconds = Math.Clamp(action.GetInt("delay_seconds", 0), 0, MaxDelaySeconds);
        if (delaySeconds > 0)
        {
            _logger.LogDebug(
                "start_raid to {TargetId} waiting {Delay}s before firing",
                targetId,
                delaySeconds
            );
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ctx.CancellationToken);
        }

        Result<TwitchRaid> raid = await _raids.StartRaidAsync(
            ctx.BroadcasterId,
            targetId,
            ctx.CancellationToken
        );
        return raid.IsSuccess
            ? ActionResult.Success($"raid started to {rawTarget}")
            : ActionResult.Failure(raid.ErrorMessage ?? $"Twitch raid API failed for {rawTarget}");
    }
}

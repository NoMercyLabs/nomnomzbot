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
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Infrastructure.Stream.PipelineActions;

/// <summary>
/// Pipeline action that starts a Twitch raid via Helix POST /raids on the broadcaster's token
/// (<c>channel:manage:raids</c>).
///
/// Parameters:
///   target        — Twitch login/channel name **or numeric user id** to raid (required; a leading @ is
///                   tolerated — resolved both from a raw literal and from a "{variable}" substitution like
///                   shoutout, e.g. "{args.1}" seeded from "!raid @someone"). A login is resolved to its id
///                   via Helix Get Users.
///   delay_seconds — Optional wait AFTER the raid has fired, clamped to 0–90 (a hype-building beat before the
///                   next pipeline step — e.g. a shoutout/announcement — runs). Honored as an INTERNAL
///                   <see cref="Task.Delay(TimeSpan, CancellationToken)"/> under the pipeline's cancellation
///                   token, not via the engine's wait step, so one block carries the whole "raid, pause,
///                   announce" beat without a separate wait action. The raid call itself is never delayed —
///                   it fires as soon as the target resolves and is confirmed live, so the raid is real by the
///                   time any chained shoutout/announcement step runs.
///
/// Typed failures: missing/unknown target, a failed (as opposed to empty) Twitch lookup, a target that isn't
/// currently live, missing scope (routed into the progressive re-grant flow — the scope pre-check that
/// produces this failure already raises <c>TwitchHelixReauthRequiredEvent</c>, so a dashboard/chat re-grant
/// prompt follows automatically), and any other Helix refusal. An "already raiding" conflict (Twitch 409 —
/// the broadcaster already has a raid in flight) is tolerated as success rather than failing the step, since
/// the desired end state (a raid to a target is under way) already holds.
///
/// On a successfully started (or already-in-flight) raid, publishes <see cref="RaidSentEvent"/> so overlay
/// alerts, Discord notifications and the event journal see a chat-initiated raid the same as an EventSub one.
///
/// Usage example:
///   { "type": "start_raid", "target": "{args.1}", "delay_seconds": 10 }
/// </summary>
public sealed class StartRaidAction : ICommandAction
{
    private const int MaxDelaySeconds = 90;

    private readonly ITwitchRaidsApi _raids;
    private readonly ITwitchUsersApi _users;
    private readonly ITwitchStreamsApi _streams;
    private readonly IEventBus _eventBus;
    private readonly ILogger<StartRaidAction> _logger;

    public string ActionType => "start_raid";
    public string Category => "stream";
    public string Description => "Start a raid to another channel";

    public StartRaidAction(
        ITwitchRaidsApi raids,
        ITwitchUsersApi users,
        ITwitchStreamsApi streams,
        IEventBus eventBus,
        ILogger<StartRaidAction> logger
    )
    {
        _raids = raids;
        _users = users;
        _streams = streams;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string? rawTarget = action.GetString("target") ?? string.Empty;

        // Resolve {variable} references inside the target param (same convention as shoutout's user_id) —
        // e.g. "{args.1}" seeded from "!raid @someone" carries the "@" straight through into the variable.
        if (rawTarget.StartsWith('{') && rawTarget.EndsWith('}'))
        {
            string key = rawTarget[1..^1];
            ctx.Variables.TryGetValue(key, out rawTarget!);
        }

        rawTarget = rawTarget?.Trim().TrimStart('@') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawTarget))
            return ActionResult.Failure("start_raid action requires a non-empty 'target'");

        // A raid command names a channel; Helix wants the numeric id — resolve a login. A failed lookup
        // (Helix/transport error) is a different problem than a lookup that succeeded but found nobody —
        // the former needs the caller to retry/investigate Twitch, the latter needs a different target name.
        string targetId = rawTarget;
        string targetDisplayName = rawTarget;
        if (!targetId.All(char.IsAsciiDigit))
        {
            Result<IReadOnlyList<TwitchUser>> lookup = await _users.GetUsersByLoginsAsync(
                [targetId.ToLowerInvariant()],
                ctx.CancellationToken
            );
            if (lookup.IsFailure)
                return ActionResult.Failure(
                    $"start_raid could not look up '{rawTarget}' on Twitch: {lookup.ErrorMessage}"
                );

            TwitchUser? resolved = lookup.Value.FirstOrDefault();
            if (resolved is null)
                return ActionResult.Failure(
                    $"start_raid target '{rawTarget}' was not found on Twitch"
                );
            targetId = resolved.Id;
            targetDisplayName = resolved.DisplayName;
        }

        // A live pre-check: raiding an offline channel is a wasted raid (Twitch drops viewers onto an
        // offline channel page with nothing to watch). Best-effort — a failed live check never blocks the
        // raid outright, only a confirmed-offline target does.
        Result<TwitchPage<TwitchStream>> liveCheck = await _streams.GetStreamsAsync(
            new TwitchStreamsFilter(UserIds: [targetId]),
            new TwitchPageRequest(PageSize: 1),
            ctx.CancellationToken
        );
        if (liveCheck.IsSuccess && liveCheck.Value.Items.Count == 0)
            return ActionResult.Failure(
                $"start_raid target '{rawTarget}' is not currently live — raid not started"
            );

        // The raid fires immediately once the target is resolved and confirmed live — never delayed. A
        // chained shoutout/announcement step must see a real, already-started raid, not a still-pending one.
        Result<TwitchRaid> raid = await _raids.StartRaidAsync(
            ctx.BroadcasterId,
            targetId,
            ctx.CancellationToken
        );

        // "Already raiding" (Twitch 409 — a raid to this or another target is already in flight) is not a
        // failure: the desired end state — a raid under way — already holds, so the step tolerates it.
        bool alreadyRaiding = raid.IsFailure && raid.ErrorCode == TwitchErrorCodes.Conflict;

        if (raid.IsFailure && !alreadyRaiding)
        {
            if (raid.ErrorCode == TwitchErrorCodes.MissingScope)
            {
                // The scope pre-check inside TwitchRaidsApi already raised TwitchHelixReauthRequiredEvent
                // (Reason = missing_scope), which the identity module turns into a recorded gap + a dashboard/
                // chat re-grant prompt — this step must not report a bare, unactionable Twitch error string.
                _logger.LogInformation(
                    "start_raid to {TargetId} needs a re-grant of channel:manage:raids",
                    targetId
                );
                return ActionResult.Failure(
                    "start_raid needs the raid permission re-granted — check the dashboard's connection status to re-authorize"
                );
            }

            return ActionResult.Failure(
                raid.ErrorMessage ?? $"Twitch raid API failed for {rawTarget}"
            );
        }

        await _eventBus.PublishAsync(
            new RaidSentEvent
            {
                BroadcasterId = ctx.BroadcasterId,
                ToUserId = targetId,
                ToDisplayName = targetDisplayName,
            },
            ctx.CancellationToken
        );

        int delaySeconds = Math.Clamp(action.GetInt("delay_seconds", 0), 0, MaxDelaySeconds);
        if (delaySeconds > 0)
        {
            _logger.LogDebug(
                "start_raid to {TargetId} fired — waiting {Delay}s before the next step",
                targetId,
                delaySeconds
            );
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ctx.CancellationToken);
        }

        return ActionResult.Success(
            alreadyRaiding
                ? $"raid already in progress to {rawTarget} — tolerated"
                : $"raid started to {rawTarget}"
        );
    }
}

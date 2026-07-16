// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Rewards.Dtos;

namespace NomNomzBot.Application.Rewards.Services;

/// <summary>
/// Countdown timers for time-limited reward redemptions ("streamer does X for Y"): a redemption of a
/// reward with a configured duration starts one; the operator can pause / resume / complete / cancel it;
/// completing (manually or by expiry) marks the redemption FULFILLED on Twitch when the reward is
/// manageable. Remaining time is clock-derived — rows are only written on state changes, and a restart
/// never loses a countdown.
/// </summary>
public interface IRedemptionTimerService
{
    /// <summary>
    /// Starts a countdown for a redemption (idempotent per redemption — a webhook redelivery returns the
    /// existing timer instead of double-starting).
    /// </summary>
    Task<Result<RedemptionTimerDto>> StartAsync(
        Guid broadcasterId,
        string redemptionId,
        string rewardId,
        string rewardTitle,
        string redeemedByDisplayName,
        int durationSeconds,
        CancellationToken cancellationToken = default
    );

    /// <summary>The channel's timers, active (running/paused) first, then recent terminal rows.</summary>
    Task<Result<IReadOnlyList<RedemptionTimerDto>>> ListAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    );

    Task<Result<RedemptionTimerDto>> PauseAsync(
        string broadcasterId,
        Guid timerId,
        CancellationToken cancellationToken = default
    );

    Task<Result<RedemptionTimerDto>> ResumeAsync(
        string broadcasterId,
        Guid timerId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Finishes the countdown now: marks it completed and fulfills the redemption on Twitch.</summary>
    Task<Result<RedemptionTimerDto>> CompleteAsync(
        string broadcasterId,
        Guid timerId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Abandons the countdown without fulfilling — refunding stays the operator's separate call.</summary>
    Task<Result<RedemptionTimerDto>> CancelAsync(
        string broadcasterId,
        Guid timerId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Completes every running timer whose countdown has reached zero (the ticker's pass).</summary>
    Task<int> CompleteDueAsync(CancellationToken cancellationToken = default);
}

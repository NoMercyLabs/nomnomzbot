// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Rewards.Entities;

/// <summary>
/// One live countdown for a redeemed time-limited reward ("streamer does X for Y"). The remaining time
/// is CLOCK-DERIVED, never tick-decremented: while running it is <see cref="RemainingSeconds"/> minus
/// the elapsed time since <see cref="RunningSince"/>; a pause folds the elapsed time into
/// <see cref="RemainingSeconds"/> and clears <see cref="RunningSince"/> — so the row is only written on
/// state CHANGES and a restart never loses the countdown. Terminal rows (completed / canceled) stay as
/// the channel's timer history.
/// </summary>
public class RedemptionTimer : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid BroadcasterId { get; set; }

    /// <summary>Twitch's redemption UUID — what completion fulfills via Helix.</summary>
    [MaxLength(50)]
    public string RedemptionId { get; set; } = null!;

    /// <summary>Twitch's reward UUID (denormalized for the fulfill call + display).</summary>
    [MaxLength(50)]
    public string RewardId { get; set; } = null!;

    [MaxLength(255)]
    public string RewardTitle { get; set; } = null!;

    [MaxLength(255)]
    public string RedeemedByDisplayName { get; set; } = null!;

    /// <summary>The full configured countdown, for display ("12:34 of 20:00").</summary>
    public int DurationSeconds { get; set; }

    /// <summary>
    /// Seconds left as of the last state change. While running, the live value is this minus the time
    /// elapsed since <see cref="RunningSince"/>; while paused it is exact.
    /// </summary>
    public int RemainingSeconds { get; set; }

    /// <summary>Set while the countdown is running; null while paused or terminal.</summary>
    public DateTime? RunningSince { get; set; }

    /// <summary>running | paused | completed | canceled.</summary>
    [MaxLength(20)]
    public string Status { get; set; } = RedemptionTimerStatus.Running;

    public DateTime StartedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>The <see cref="RedemptionTimer.Status"/> vocabulary.</summary>
public static class RedemptionTimerStatus
{
    public const string Running = "running";
    public const string Paused = "paused";
    public const string Completed = "completed";
    public const string Canceled = "canceled";
}

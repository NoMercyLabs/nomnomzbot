// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Stream.Events;

/// <summary>
/// Published on every poll of the channel's ad schedule while it is live (old bot: widget event
/// <c>twitch.ad.schedule</c>). Carries the whole schedule so an overlay can draw a countdown.
/// </summary>
public sealed class AdScheduleUpdatedEvent : DomainEventBase
{
    /// <summary>When the next ad is due, or <c>null</c> when none is scheduled.</summary>
    public DateTimeOffset? NextAdAt { get; init; }

    /// <summary>When the last ad ran, or <c>null</c> when none ran yet.</summary>
    public DateTimeOffset? LastAdAt { get; init; }

    /// <summary>The length of the next ad, in seconds.</summary>
    public required int DurationSeconds { get; init; }

    /// <summary>The pre-roll free time that is left, in seconds.</summary>
    public required int PrerollFreeTimeSeconds { get; init; }

    /// <summary>How many snoozes are left.</summary>
    public required int SnoozeCount { get; init; }

    /// <summary>When the snooze allowance refreshes, or <c>null</c> when unknown.</summary>
    public DateTimeOffset? SnoozeRefreshAt { get; init; }

    /// <summary>Seconds until the next ad at poll time, or <c>null</c> when no ad is upcoming.</summary>
    public int? TimeUntilNextAdSeconds { get; init; }
}

/// <summary>
/// Published once per warn threshold (5 minutes, 2 minutes, 60, 30 and 10 seconds) for each next-ad slot
/// (old bot: widget event <c>twitch.ad.upcoming</c>).
/// </summary>
public sealed class AdBreakUpcomingEvent : DomainEventBase
{
    /// <summary>Seconds left until the ad when the threshold was crossed.</summary>
    public required int SecondsUntilAd { get; init; }

    /// <summary>The threshold that was crossed, in seconds.</summary>
    public required int ThresholdSeconds { get; init; }

    /// <summary>The length of the coming ad, in seconds.</summary>
    public required int DurationSeconds { get; init; }

    /// <summary>When the ad is due.</summary>
    public required DateTimeOffset NextAdAt { get; init; }
}

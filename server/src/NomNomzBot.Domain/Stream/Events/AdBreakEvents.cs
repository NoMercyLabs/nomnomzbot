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
/// Published when a mid-roll ad break starts (EventSub <c>channel.ad_break.begin</c>). Carries the break length
/// and whether Twitch scheduled it automatically; the requester fields are present only for a manually started
/// break. Inherits EventId / Timestamp / BroadcasterId from DomainEventBase (publisher sets the resolved tenant).
/// </summary>
public sealed class AdBreakBeganEvent : DomainEventBase
{
    /// <summary>How long the ad break runs, in seconds.</summary>
    public required int DurationSeconds { get; init; }

    /// <summary>True when Twitch started the break by itself, and false when someone started it by hand.</summary>
    public required bool IsAutomatic { get; init; }

    /// <summary>When the ad break started, in UTC time.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>The user who started the break, or <c>null</c> for an automatic break.</summary>
    public string? RequesterUserId { get; init; }

    /// <summary>The display name of the user who started the break, or <c>null</c> for an automatic break.</summary>
    public string? RequesterDisplayName { get; init; }
}

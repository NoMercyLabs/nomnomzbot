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

namespace NomNomzBot.Domain.Rewards.Events;

/// <summary>
/// Published when a viewer reaches a watch streak milestone.
/// Sourced from EventSub <c>channel.chat.notification</c> (notice_type=watch_streak), translated by
/// <c>ChatTranslators</c> — not IRC, which is fully retired.
/// </summary>
public sealed class WatchStreakReceivedEvent : DomainEventBase
{
    /// <summary>The Twitch user id of the viewer (a number as text).</summary>
    public required string UserId { get; init; }

    /// <summary>The viewer's login name (lowercase).</summary>
    public required string UserLogin { get; init; }

    /// <summary>The viewer's display name, as shown in chat.</summary>
    public required string UserDisplayName { get; init; }

    /// <summary>How many months in a row the viewer has watched.</summary>
    public required int StreakMonths { get; init; }

    /// <summary>The channel points the viewer earned for the streak.</summary>
    public required int ChannelPointsEarned { get; init; }

    /// <summary>The message the viewer shared with the streak. Empty when they wrote none.</summary>
    public string? CustomMessage { get; init; }
}

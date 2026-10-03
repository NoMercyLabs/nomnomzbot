// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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

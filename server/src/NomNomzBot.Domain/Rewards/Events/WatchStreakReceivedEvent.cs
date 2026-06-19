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
/// Sourced from IRC USERNOTICE with msg-id=viewermilestone.
/// </summary>
public sealed class WatchStreakReceivedEvent : DomainEventBase
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserDisplayName { get; init; }
    public required int StreakMonths { get; init; }
    public required int ChannelPointsEarned { get; init; }
    public string? CustomMessage { get; init; }
}

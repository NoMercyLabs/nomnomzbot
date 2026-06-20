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

namespace NomNomzBot.Domain.Community.Events;

/// <summary>
/// Published when a broadcaster starts a creator goal (<c>channel.goal.begin</c>). <see cref="Type"/> is Twitch's
/// goal kind — e.g. <c>follower</c>, <c>subscription</c>, <c>subscription_count</c>, <c>new_subscription</c>,
/// <c>new_subscription_count</c>.
/// </summary>
public sealed class GoalBeganEvent : DomainEventBase
{
    public required string GoalId { get; init; }
    public required string Type { get; init; }
    public required string Description { get; init; }
    public required int CurrentAmount { get; init; }
    public required int TargetAmount { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
}

/// <summary>
/// Published when progress (positive or negative) is made toward a goal (<c>channel.goal.progress</c>); carries
/// the updated <see cref="CurrentAmount"/>.
/// </summary>
public sealed class GoalProgressEvent : DomainEventBase
{
    public required string GoalId { get; init; }
    public required string Type { get; init; }
    public required string Description { get; init; }
    public required int CurrentAmount { get; init; }
    public required int TargetAmount { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
}

/// <summary>
/// Published when a broadcaster ends a goal (<c>channel.goal.end</c>); <see cref="IsAchieved"/> reports whether
/// the target was met.
/// </summary>
public sealed class GoalEndedEvent : DomainEventBase
{
    public required string GoalId { get; init; }
    public required string Type { get; init; }
    public required string Description { get; init; }
    public required int CurrentAmount { get; init; }
    public required int TargetAmount { get; init; }
    public required bool IsAchieved { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset EndedAt { get; init; }
}

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
    /// <summary>The id of the goal.</summary>
    public required string GoalId { get; init; }

    /// <summary>The kind of goal, as Twitch names it. The bot passes it on unchanged.</summary>
    public required string Type { get; init; }

    /// <summary>The description of the goal. Empty when none is set.</summary>
    public required string Description { get; init; }

    /// <summary>How far the goal has come so far, in the goal's own unit.</summary>
    public required int CurrentAmount { get; init; }

    /// <summary>The number the goal aims for, in the goal's own unit.</summary>
    public required int TargetAmount { get; init; }

    /// <summary>When the goal started, in UTC time.</summary>
    public required DateTimeOffset StartedAt { get; init; }
}

/// <summary>
/// Published when progress (positive or negative) is made toward a goal (<c>channel.goal.progress</c>); carries
/// the updated <see cref="CurrentAmount"/>.
/// </summary>
public sealed class GoalProgressEvent : DomainEventBase
{
    /// <summary>The id of the goal.</summary>
    public required string GoalId { get; init; }

    /// <summary>The kind of goal, as Twitch names it. The bot passes it on unchanged.</summary>
    public required string Type { get; init; }

    /// <summary>The description of the goal. Empty when none is set.</summary>
    public required string Description { get; init; }

    /// <summary>How far the goal has come so far, in the goal's own unit.</summary>
    public required int CurrentAmount { get; init; }

    /// <summary>The number the goal aims for, in the goal's own unit.</summary>
    public required int TargetAmount { get; init; }

    /// <summary>When the goal started, in UTC time.</summary>
    public required DateTimeOffset StartedAt { get; init; }
}

/// <summary>
/// Published when a broadcaster ends a goal (<c>channel.goal.end</c>); <see cref="IsAchieved"/> reports whether
/// the target was met.
/// </summary>
public sealed class GoalEndedEvent : DomainEventBase
{
    /// <summary>The id of the goal.</summary>
    public required string GoalId { get; init; }

    /// <summary>The kind of goal, as Twitch names it. The bot passes it on unchanged.</summary>
    public required string Type { get; init; }

    /// <summary>The description of the goal. Empty when none is set.</summary>
    public required string Description { get; init; }

    /// <summary>How far the goal has come so far, in the goal's own unit.</summary>
    public required int CurrentAmount { get; init; }

    /// <summary>The number the goal aims for, in the goal's own unit.</summary>
    public required int TargetAmount { get; init; }

    /// <summary>True when the goal reached its target.</summary>
    public required bool IsAchieved { get; init; }

    /// <summary>When the goal started, in UTC time.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>When the goal ended, in UTC time.</summary>
    public required DateTimeOffset EndedAt { get; init; }
}

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

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

/// <summary>Published when a hype train begins (<c>channel.hype_train.begin</c> v2).</summary>
public sealed class HypeTrainBeganEvent : DomainEventBase
{
    /// <summary>The id of the hype train.</summary>
    public required string HypeTrainId { get; init; }

    /// <summary>The current level of the hype train.</summary>
    public required int Level { get; init; }

    /// <summary>The total points the hype train has earned so far.</summary>
    public required int Total { get; init; }

    /// <summary>The points earned toward the next level.</summary>
    public required int Progress { get; init; }

    /// <summary>The points needed to reach the next level.</summary>
    public required int Goal { get; init; }

    /// <summary>The top contributors to the hype train, with what each gave.</summary>
    public required IReadOnlyList<HypeTrainContribution> TopContributions { get; init; }

    /// <summary>When the hype train ends if nobody adds more, in UTC time.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>Published on each <c>channel.hype_train.progress</c> tick (v2) as the train advances.</summary>
public sealed class HypeTrainProgressEvent : DomainEventBase
{
    /// <summary>The id of the hype train.</summary>
    public required string HypeTrainId { get; init; }

    /// <summary>The current level of the hype train.</summary>
    public required int Level { get; init; }

    /// <summary>The total points the hype train has earned so far.</summary>
    public required int Total { get; init; }

    /// <summary>The points earned toward the next level.</summary>
    public required int Progress { get; init; }

    /// <summary>The points needed to reach the next level.</summary>
    public required int Goal { get; init; }

    /// <summary>The top contributors to the hype train, with what each gave.</summary>
    public required IReadOnlyList<HypeTrainContribution> TopContributions { get; init; }

    /// <summary>When the hype train ends if nobody adds more, in UTC time.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>Published when a hype train ends (<c>channel.hype_train.end</c> v2) with the final level reached.</summary>
public sealed class HypeTrainEndedEvent : DomainEventBase
{
    /// <summary>The id of the hype train.</summary>
    public required string HypeTrainId { get; init; }

    /// <summary>The current level of the hype train.</summary>
    public required int Level { get; init; }

    /// <summary>The total points the hype train has earned so far.</summary>
    public required int Total { get; init; }

    /// <summary>The top contributors to the hype train, with what each gave.</summary>
    public required IReadOnlyList<HypeTrainContribution> TopContributions { get; init; }

    /// <summary>When the hype train ended, in UTC time.</summary>
    public required DateTimeOffset EndedAt { get; init; }
}

/// <summary>
/// A single hype train contributor entry (top_contributions / last_contribution). <paramref name="Type"/> is
/// Twitch's contribution kind — <c>bits</c>, <c>subscription</c>, or <c>other</c>; <paramref name="Total"/> is the
/// amount in that type's own unit (bits count, or sub-equivalent value).
/// </summary>
/// <param name="UserId">The Twitch user id of the contributor (a number as text).</param>
/// <param name="UserLogin">The login name of the contributor (lowercase).</param>
/// <param name="UserDisplayName">The display name of the contributor, as shown in chat.</param>
/// <param name="Type">The kind of contribution: bits, subscription or other.</param>
/// <param name="Total">The amount in the unit of that kind: a bits count, or sub-equivalent value.</param>
public sealed record HypeTrainContribution(
    string UserId,
    string UserLogin,
    string UserDisplayName,
    string Type,
    int Total
);

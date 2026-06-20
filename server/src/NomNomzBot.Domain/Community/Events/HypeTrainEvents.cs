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
    public required string HypeTrainId { get; init; }
    public required int Level { get; init; }
    public required int Total { get; init; }
    public required int Progress { get; init; }
    public required int Goal { get; init; }
    public required IReadOnlyList<HypeTrainContribution> TopContributions { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>Published on each <c>channel.hype_train.progress</c> tick (v2) as the train advances.</summary>
public sealed class HypeTrainProgressEvent : DomainEventBase
{
    public required string HypeTrainId { get; init; }
    public required int Level { get; init; }
    public required int Total { get; init; }
    public required int Progress { get; init; }
    public required int Goal { get; init; }
    public required IReadOnlyList<HypeTrainContribution> TopContributions { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>Published when a hype train ends (<c>channel.hype_train.end</c> v2) with the final level reached.</summary>
public sealed class HypeTrainEndedEvent : DomainEventBase
{
    public required string HypeTrainId { get; init; }
    public required int Level { get; init; }
    public required int Total { get; init; }
    public required IReadOnlyList<HypeTrainContribution> TopContributions { get; init; }
    public required DateTimeOffset EndedAt { get; init; }
}

/// <summary>
/// A single hype train contributor entry (top_contributions / last_contribution). <paramref name="Type"/> is
/// Twitch's contribution kind — <c>bits</c>, <c>subscription</c>, or <c>other</c>; <paramref name="Total"/> is the
/// amount in that type's own unit (bits count, or sub-equivalent value).
/// </summary>
public sealed record HypeTrainContribution(
    string UserId,
    string UserLogin,
    string UserDisplayName,
    string Type,
    int Total
);

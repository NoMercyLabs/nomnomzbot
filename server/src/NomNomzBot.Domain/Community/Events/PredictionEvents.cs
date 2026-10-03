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

/// <summary>Published when a prediction opens for voting.</summary>
public sealed class PredictionBeganEvent : DomainEventBase
{
    /// <summary>The id of the prediction.</summary>
    public required string PredictionId { get; init; }

    /// <summary>The prediction question as shown to viewers.</summary>
    public required string Title { get; init; }

    /// <summary>The outcomes viewers can pick, each with its channel points and number of users.</summary>
    public required IReadOnlyList<PredictionOutcome> Outcomes { get; init; }

    /// <summary>How long viewers can vote, in seconds.</summary>
    public required int WindowSeconds { get; init; }

    /// <summary>When voting closes, in UTC time.</summary>
    public required DateTimeOffset LocksAt { get; init; }
}

/// <summary>Published on each <c>channel.prediction.progress</c> tick while a prediction is open (running pools).</summary>
public sealed class PredictionProgressEvent : DomainEventBase
{
    /// <summary>The id of the prediction.</summary>
    public required string PredictionId { get; init; }

    /// <summary>The prediction question as shown to viewers.</summary>
    public required string Title { get; init; }

    /// <summary>The outcomes viewers can pick, each with its channel points and number of users.</summary>
    public required IReadOnlyList<PredictionOutcome> Outcomes { get; init; }

    /// <summary>When voting closes, in UTC time.</summary>
    public required DateTimeOffset LocksAt { get; init; }
}

/// <summary>Published when voting is locked (but not yet resolved).</summary>
public sealed class PredictionLockedEvent : DomainEventBase
{
    /// <summary>The id of the prediction.</summary>
    public required string PredictionId { get; init; }

    /// <summary>The prediction question as shown to viewers.</summary>
    public required string Title { get; init; }

    /// <summary>The outcomes viewers can pick, each with its channel points and number of users.</summary>
    public required IReadOnlyList<PredictionOutcome> Outcomes { get; init; }
}

/// <summary>Published when a prediction is resolved or cancelled.</summary>
public sealed class PredictionEndedEvent : DomainEventBase
{
    /// <summary>The id of the prediction.</summary>
    public required string PredictionId { get; init; }

    /// <summary>The prediction question as shown to viewers.</summary>
    public required string Title { get; init; }

    /// <summary>How the prediction ended: resolved or canceled.</summary>
    public required string Status { get; init; }

    /// <summary>The outcomes viewers can pick, each with its channel points and number of users.</summary>
    public required IReadOnlyList<PredictionOutcome> Outcomes { get; init; }

    /// <summary>The id of the winning outcome. Empty when the prediction was cancelled.</summary>
    public string? WinningOutcomeId { get; init; }
}

/// <summary>One outcome of a prediction with the points and users behind it.</summary>
/// <param name="Id">The Twitch id of the outcome.</param>
/// <param name="Title">The text of the outcome, as viewers see it.</param>
/// <param name="ChannelPoints">The total channel points viewers spent on this outcome.</param>
/// <param name="Users">The number of viewers who picked this outcome.</param>
/// <param name="Color">The colour of the outcome: blue or pink.</param>
public sealed record PredictionOutcome(
    string Id,
    string Title,
    int ChannelPoints,
    int Users,
    string Color
);

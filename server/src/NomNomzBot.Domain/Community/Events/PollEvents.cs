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

/// <summary>Published when a poll begins.</summary>
public sealed class PollBeganEvent : DomainEventBase
{
    /// <summary>The id of the poll.</summary>
    public required string PollId { get; init; }

    /// <summary>The poll question as shown to viewers.</summary>
    public required string Title { get; init; }

    /// <summary>The poll choices, each with its vote counts.</summary>
    public required IReadOnlyList<PollChoice> Choices { get; init; }

    /// <summary>How long the poll runs, in seconds.</summary>
    public required int DurationSeconds { get; init; }

    /// <summary>When the poll ends, in UTC time.</summary>
    public required DateTimeOffset EndsAt { get; init; }
}

/// <summary>Published on each <c>channel.poll.progress</c> tick while a poll is open (running vote tallies).</summary>
public sealed class PollProgressEvent : DomainEventBase
{
    /// <summary>The id of the poll.</summary>
    public required string PollId { get; init; }

    /// <summary>The poll question as shown to viewers.</summary>
    public required string Title { get; init; }

    /// <summary>The poll choices, each with its vote counts.</summary>
    public required IReadOnlyList<PollChoice> Choices { get; init; }

    /// <summary>When the poll ends, in UTC time.</summary>
    public required DateTimeOffset EndsAt { get; init; }
}

/// <summary>Published when a poll ends (terminal states: completed, archived, terminated).</summary>
public sealed class PollEndedEvent : DomainEventBase
{
    /// <summary>The id of the poll.</summary>
    public required string PollId { get; init; }

    /// <summary>The poll question as shown to viewers.</summary>
    public required string Title { get; init; }

    /// <summary>How the poll ended: completed, archived or terminated.</summary>
    public required string Status { get; init; }

    /// <summary>The final poll choices, each with its vote counts.</summary>
    public required IReadOnlyList<PollChoice> Choices { get; init; }

    /// <summary>Id of the choice with the most total votes, or <c>null</c> when there were no votes.</summary>
    public string? WinningChoiceId { get; init; }
}

public sealed record PollChoice(string Id, string Title, int Votes, int ChannelPointsVotes);

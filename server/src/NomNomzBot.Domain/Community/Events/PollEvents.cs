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

/// <summary>One choice of a poll with its vote counts.</summary>
/// <param name="Id">The Twitch id of the choice.</param>
/// <param name="Title">The text of the choice, as viewers see it.</param>
/// <param name="Votes">The total number of votes for the choice.</param>
/// <param name="ChannelPointsVotes">How many of those votes viewers paid for with channel points.</param>
public sealed record PollChoice(string Id, string Title, int Votes, int ChannelPointsVotes);

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
    public required string PollId { get; init; }
    public required string Title { get; init; }
    public required IReadOnlyList<PollChoice> Choices { get; init; }
    public required int DurationSeconds { get; init; }
    public required DateTimeOffset EndsAt { get; init; }
}

/// <summary>Published when a poll ends (terminal states: completed, archived, terminated).</summary>
public sealed class PollEndedEvent : DomainEventBase
{
    public required string PollId { get; init; }
    public required string Title { get; init; }
    public required string Status { get; init; }
    public required IReadOnlyList<PollChoice> Choices { get; init; }
}

public sealed record PollChoice(string Id, string Title, int Votes, int ChannelPointsVotes);

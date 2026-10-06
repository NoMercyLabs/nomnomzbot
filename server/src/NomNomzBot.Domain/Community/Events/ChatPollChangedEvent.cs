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
/// A bot-run chat poll changed: it opened, a vote moved a tally, or it closed. Carries the whole poll as it
/// stands so the dashboard can redraw the bars without calling the API. It never leaves the server as an SDK event.
/// </summary>
[Event(visibility: EventVisibility.Internal)]
public sealed class ChatPollChangedEvent : DomainEventBase
{
    /// <summary>The id of the poll.</summary>
    public required Guid PollId { get; init; }

    /// <summary>What happened: <c>opened</c>, <c>voted</c> or <c>closed</c>.</summary>
    public required string Change { get; init; }

    /// <summary>The poll question as shown to viewers.</summary>
    public required string Question { get; init; }

    /// <summary>The poll status after the change: <c>open</c> or <c>closed</c>.</summary>
    public required string Status { get; init; }

    /// <summary>The number of votes cast across all options.</summary>
    public required int TotalVotes { get; init; }

    /// <summary>The options with their current vote counts.</summary>
    public required IReadOnlyList<ChatPollChangedOption> Options { get; init; }

    /// <summary>When the poll opened, in UTC time.</summary>
    public required DateTime OpenedAt { get; init; }

    /// <summary>When the poll closes by itself, in UTC time. Null when it runs until closed by hand.</summary>
    public DateTime? ClosesAt { get; init; }

    /// <summary>When the poll was closed, in UTC time. Null while it is open.</summary>
    public DateTime? ClosedAt { get; init; }
}

/// <summary>One option of a changed chat poll, with its current vote count.</summary>
/// <param name="Index">The 1-based option number viewers type to vote.</param>
/// <param name="Label">The option text.</param>
/// <param name="Votes">The votes cast for this option.</param>
public sealed record ChatPollChangedOption(int Index, string Label, int Votes);

// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.EventStore;

/// <summary>
/// One journal row as the dashboard lists it. The payload is deliberately absent: it can hold sealed personal
/// data, and the list only needs to show what happened, when, where it came from and who did it.
/// </summary>
public sealed record EventJournalEntryDto(
    Guid EventId,
    long StreamPosition,
    string EventType,
    string Source,
    DateTime OccurredAt,
    DateTime RecordedAt,
    Guid? ActorUserId,
    string? ActorExternalUserId,
    string? ActorProvider
)
{
    public static EventJournalEntryDto From(EventRecord record) =>
        new(
            record.EventId,
            record.StreamPosition,
            record.EventType,
            record.Source,
            record.OccurredAt,
            record.RecordedAt,
            record.ActorUserId,
            record.ActorExternalUserId,
            record.ActorProvider
        );
}

/// <summary>
/// One page of a channel's journal, newest first, with the figures the journal page shows beside the list:
/// how many rows match the filters and where the channel's stream currently ends.
/// </summary>
public sealed record EventJournalPageDto(
    IReadOnlyList<EventJournalEntryDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    bool HasMore,
    long HeadPosition
);

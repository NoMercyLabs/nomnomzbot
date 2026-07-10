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

// ---- Append ----

/// <summary>
/// One event to append to the journal. <c>PayloadJson</c>/<c>MetadataJson</c> are already serialized by the
/// caller (Newtonsoft.Json). The journal allocates the per-tenant <c>StreamPosition</c> on append.
/// </summary>
public sealed record AppendEventRequest(
    Guid EventId,
    Guid? BroadcasterId,
    string EventType,
    int EventVersion,
    string Source, // eventsub|domain|irc|import|federation|webhook
    string PayloadJson,
    string MetadataJson,
    DateTime OccurredAt,
    Guid? CorrelationId = null,
    Guid? CausationId = null,
    Guid? ActorUserId = null,
    string? ActorExternalUserId = null,
    string? ActorProvider = null // twitch|kick|youtube|twitter — the namespace of ActorExternalUserId
);

/// <summary>An immutable journal row as read back by projections / replay / the audit UI.</summary>
public sealed record EventRecord(
    long Id,
    Guid EventId,
    Guid? BroadcasterId,
    long StreamPosition,
    string EventType,
    int EventVersion,
    string Source,
    string PayloadJson,
    bool PayloadIsEncrypted,
    Guid? SubjectKeyId,
    Guid? CorrelationId,
    Guid? CausationId,
    Guid? ActorUserId,
    string? ActorExternalUserId,
    string? ActorProvider,
    string MetadataJson,
    DateTime OccurredAt,
    DateTime RecordedAt
);

// ---- Journal query (audit UI) ----

public sealed record EventJournalQuery(
    Guid? BroadcasterId,
    string? EventType,
    DateTime? FromUtc,
    DateTime? ToUtc,
    Guid? ActorUserId,
    int Page,
    int PageSize
);

// ---- Projections ----

public sealed record ProjectionCheckpointDto(
    string ProjectionName,
    Guid? BroadcasterId,
    long LastPosition,
    long HeadPosition,
    long Lag, // HeadPosition - LastPosition
    string Status, // running|rebuilding|faulted|paused
    string? LastError,
    DateTime? LastProcessedAt,
    DateTime UpdatedAt
);

// ---- Upcasting ----

public sealed record UpcastResult(string PayloadJson, int ToVersion, bool Changed);

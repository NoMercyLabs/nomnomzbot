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
    string? ActorProvider = null, // twitch|kick|youtube|twitter — the namespace of ActorExternalUserId
    // Act-as impersonation dual-actor trail (S089a): when the write was made under impersonation,
    // ActorUserId names the OPERATOR and these two name who they were acting as and under which
    // support session — so an impersonated write never loses its true author.
    Guid? OnBehalfOfUserId = null,
    Guid? ImpersonationSessionId = null
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
    DateTime RecordedAt,
    // Act-as impersonation dual-actor trail (S089a) — see AppendEventRequest. Trailing + optional so
    // every existing positional AND named-arg construction of this record stays source-compatible.
    Guid? OnBehalfOfUserId = null,
    Guid? ImpersonationSessionId = null
);

// ---- Journal query (audit UI) ----

public sealed record EventJournalQuery(
    Guid? BroadcasterId,
    string? EventType,
    DateTime? FromUtc,
    DateTime? ToUtc,
    Guid? ActorUserId,
    int Page,
    int PageSize,
    string? Source = null
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

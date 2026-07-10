// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.EventStore;

/// <summary>
/// Portable export/import of one tenant's slice of the append-only <see cref="IEventJournal"/>. EXPORT serializes
/// the tenant's stream to JSONL (one full event envelope per line, in <c>StreamPosition</c> order). IMPORT reads
/// such a file back into the <em>caller's</em> tenant: each line is upcast to its event type's current shape
/// (<see cref="IEventUpcasterRegistry"/>) and appended idempotently (dedup by <c>EventId</c>; a re-import of an
/// already-present event consumes no new position and is counted as a skip). The whole import is wrapped in one
/// <c>IUnitOfWork</c> transaction — all-or-nothing — and never crosses tenants: the importing tenant's
/// <c>BroadcasterId</c> is stamped on every appended row and a fresh per-tenant <c>StreamPosition</c> is allocated,
/// so the source file's positions/tenant are advisory only and a foreign tenant's events can never leak in.
/// </summary>
public interface IEventJournalPortabilityService
{
    /// <summary>
    /// Streams the tenant's whole journal to <paramref name="destination"/> as JSONL — one
    /// <see cref="EventJournalExportLine"/> per line, ascending by <c>StreamPosition</c>. Read-only; reads the
    /// stream in forward batches so a large journal never materializes in memory. Returns the number of lines
    /// written.
    /// </summary>
    Task<Result<long>> ExportAsync(
        Guid broadcasterId,
        Stream destination,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reads a JSONL export from <paramref name="source"/> and appends every event to <paramref name="targetBroadcasterId"/>'s
    /// stream idempotently and atomically. Each line is parsed, upcast to its current shape, re-tenanted to the
    /// target, then appended via <c>AppendBatchAsync</c> (one transaction; duplicates by <c>EventId</c> are
    /// no-ops). A malformed line fails the whole import (rollback). Returns import/skip/upcast counts.
    /// </summary>
    Task<Result<EventJournalImportSummary>> ImportAsync(
        Guid targetBroadcasterId,
        Stream source,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// One line of a JSONL journal export — the full event envelope as a portable, self-describing record. Mirrors the
/// load-bearing <see cref="EventRecord"/> fields; the source-relative <c>Id</c>/<c>StreamPosition</c> are carried
/// for provenance only — import re-allocates fresh positions in the target tenant and never trusts them.
/// </summary>
public sealed record EventJournalExportLine(
    long Id,
    Guid EventId,
    Guid? BroadcasterId,
    long StreamPosition,
    string EventType,
    int EventVersion,
    string Source,
    string PayloadJson,
    string MetadataJson,
    Guid? CorrelationId,
    Guid? CausationId,
    Guid? ActorUserId,
    string? ActorExternalUserId,
    string? ActorProvider,
    DateTime OccurredAt,
    DateTime RecordedAt
);

/// <summary>
/// The outcome of an import: how many lines the file held, how many events were newly appended, how many were
/// skipped as already-present duplicates (idempotency), and how many were upcast from a stale shape on the way in.
/// </summary>
public sealed record EventJournalImportSummary(
    long TotalLines,
    long Imported,
    long SkippedDuplicate,
    long Upcast
);

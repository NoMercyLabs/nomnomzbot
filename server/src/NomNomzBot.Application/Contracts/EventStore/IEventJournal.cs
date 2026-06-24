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
/// The append-only journal: append (tenant-scoped, assigned a monotonic <c>StreamPosition</c>) + ordered read
/// (by tenant + position range, by global id, by event id). There is no update or delete — the journal is
/// immutable and is the sole replay/projection source of truth.
/// </summary>
public interface IEventJournal
{
    /// <summary>
    /// Persists one event as the next per-tenant <c>StreamPosition</c>. The position is allocated via
    /// <see cref="ITenantSequenceAllocator"/> in the SAME transaction as the insert. Idempotent on
    /// <c>EventId</c>: a duplicate is a no-op success returning the existing record.
    /// </summary>
    Task<Result<EventRecord>> AppendAsync(
        AppendEventRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Atomically appends a batch in one transaction with contiguous per-tenant <c>StreamPosition</c>s.
    /// All-or-nothing; idempotent per <c>EventId</c> within the batch (duplicates resolve to existing rows
    /// without consuming a new position).
    /// </summary>
    Task<Result<IReadOnlyList<EventRecord>>> AppendBatchAsync(
        IReadOnlyList<AppendEventRequest> requests,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reads a forward-ordered slice of one tenant's stream by <c>StreamPosition</c> (exclusive
    /// <paramref name="afterPosition"/>), ordered ascending, at most <paramref name="limit"/> records.
    /// </summary>
    Task<Result<IReadOnlyList<EventRecord>>> ReadStreamAsync(
        Guid? broadcasterId,
        long afterPosition,
        int limit,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reads the global cross-tenant stream by the <c>bigint</c> <c>Id</c> (exclusive <paramref name="afterId"/>),
    /// ordered ascending, at most <paramref name="limit"/> records — used for platform-global projections.
    /// </summary>
    Task<Result<IReadOnlyList<EventRecord>>> ReadAllAsync(
        long afterId,
        int limit,
        CancellationToken cancellationToken = default
    );

    /// <summary>Looks up a single event by its <c>EventId</c> (dedupe/lineage/trace). NOT_FOUND if absent.</summary>
    Task<Result<EventRecord>> GetByEventIdAsync(
        Guid eventId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns the subset of <paramref name="candidateEventIds"/> that already exist in the journal — one bulk query
    /// for idempotent batch writers (the legacy import) to dedupe ~tens-of-thousands of rows without a per-row probe.
    /// </summary>
    Task<Result<IReadOnlySet<Guid>>> GetExistingEventIdsAsync(
        IReadOnlyCollection<Guid> candidateEventIds,
        CancellationToken cancellationToken = default
    );

    /// <summary>Current head <c>StreamPosition</c> for a tenant (0 if no events). Drives "up to date" checks.</summary>
    Task<Result<long>> GetHeadPositionAsync(
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Filtered/paged read for the audit UI (by EventType/time-range/actor). Read-only.</summary>
    Task<Result<PagedList<EventRecord>>> QueryAsync(
        EventJournalQuery query,
        CancellationToken cancellationToken = default
    );
}

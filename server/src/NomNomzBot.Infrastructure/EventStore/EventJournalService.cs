// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.EventStore.Entities;

namespace NomNomzBot.Infrastructure.EventStore;

/// <summary>
/// The append-only journal service (<see cref="IEventJournal"/>). Append allocates the next per-tenant
/// <c>StreamPosition</c> via <see cref="ITenantSequenceAllocator"/> IN THE SAME transaction as the insert, so
/// position assignment and the row commit are atomic. Idempotent on <c>EventId</c>: a duplicate is a no-op
/// success returning the already-stored record. Reads are forward-ordered slices for projections/replay; the
/// journal is read across tenants by design and is not ambient tenant-filtered, so reads apply an explicit
/// <c>BroadcasterId</c> predicate (<c>null</c> = the platform-global stream).
/// </summary>
public sealed class EventJournalService : IEventJournal
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantSequenceAllocator _sequences;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public EventJournalService(
        IApplicationDbContext db,
        ITenantSequenceAllocator sequences,
        IUnitOfWork unitOfWork,
        TimeProvider clock
    )
    {
        _db = db;
        _sequences = sequences;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<EventRecord>> AppendAsync(
        AppendEventRequest request,
        CancellationToken cancellationToken = default
    )
    {
        EventJournal? existing = await FindByEventIdAsync(request.EventId, cancellationToken);
        if (existing is not null)
            return Result.Success(Map(existing));

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            Result<EventJournal> appended = await AppendOneAsync(request, cancellationToken);
            if (appended.IsFailure)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<EventRecord>(
                    appended.ErrorMessage!,
                    appended.ErrorCode,
                    appended.ErrorDetail
                );
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            return Result.Success(Map(appended.Value));
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            return Result.Failure<EventRecord>(
                "Failed to append event to the journal.",
                "JOURNAL_APPEND_FAILED",
                ex.Message
            );
        }
    }

    public async Task<Result<IReadOnlyList<EventRecord>>> AppendBatchAsync(
        IReadOnlyList<AppendEventRequest> requests,
        CancellationToken cancellationToken = default
    )
    {
        if (requests.Count == 0)
            return Result.Success<IReadOnlyList<EventRecord>>([]);

        // Dedupe the WHOLE batch against the DB in one chunked query instead of one lookup per row. The stored
        // rows are loaded (not just their ids) so a duplicate returns the already-stored record — the journal's
        // idempotency contract — without re-reading per row.
        Dictionary<Guid, EventJournal> stored = await LoadStoredByEventIdAsync(
            requests.Select(r => r.EventId).Distinct().ToList(),
            cancellationToken
        );

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            // The genuinely-new requests in arrival order (first occurrence wins; a later in-batch repeat of the
            // same EventId reuses the first's row), grouped by sequence tenant so each tenant's positions come
            // from ONE block reservation — arrival order within a tenant keeps positions monotonic-by-arrival,
            // exactly as the old per-row NextAsync assigned them.
            HashSet<Guid> seen = [];
            Dictionary<Guid, List<AppendEventRequest>> newByTenant = [];
            foreach (AppendEventRequest request in requests)
            {
                if (stored.ContainsKey(request.EventId) || !seen.Add(request.EventId))
                    continue;
                Guid sequenceTenant = request.BroadcasterId ?? Guid.Empty;
                if (!newByTenant.TryGetValue(sequenceTenant, out List<AppendEventRequest>? slice))
                    newByTenant[sequenceTenant] = slice = [];
                slice.Add(request);
            }

            DateTime now = _clock.GetUtcNow().UtcDateTime;
            Dictionary<Guid, EventJournal> appended = [];
            List<EventJournal> toInsert = new(seen.Count);
            foreach ((Guid sequenceTenant, List<AppendEventRequest> slice) in newByTenant)
            {
                // One block reservation for the whole tenant slice (replaces the per-row NextAsync); the caller
                // assigns first..first+count-1 in arrival order.
                Result<long> block = await _sequences.NextBlockAsync(
                    sequenceTenant,
                    ITenantSequenceAllocator.EventStreamPositionSequence,
                    slice.Count,
                    cancellationToken
                );
                if (block.IsFailure)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return Result.Failure<IReadOnlyList<EventRecord>>(
                        block.ErrorMessage!,
                        block.ErrorCode,
                        block.ErrorDetail
                    );
                }

                long position = block.Value;
                foreach (AppendEventRequest request in slice)
                {
                    EventJournal entity = BuildJournalEntity(request, position++, now);
                    appended[request.EventId] = entity;
                    toInsert.Add(entity);
                }
            }

            if (toInsert.Count > 0)
            {
                await _db.EventJournals.AddRangeAsync(toInsert, cancellationToken);
                // ONE flush for the entire batch (was one SaveChanges per row).
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            // Results in original request order: a duplicate (stored or earlier-in-batch) returns its existing
            // record; a new one returns the row just appended.
            List<EventRecord> results = new(requests.Count);
            foreach (AppendEventRequest request in requests)
                results.Add(
                    Map(
                        stored.TryGetValue(request.EventId, out EventJournal? existing)
                            ? existing
                            : appended[request.EventId]
                    )
                );

            // Detach everything this batch tracked so a long backfill stays O(batch), not O(total) (the scoped
            // DbContext would otherwise accumulate every appended row). Rows are committed and reads use
            // AsNoTracking, so dropping the tracked graph is safe.
            ClearChangeTracker();

            return Result.Success<IReadOnlyList<EventRecord>>(results);
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            return Result.Failure<IReadOnlyList<EventRecord>>(
                "Failed to append event batch to the journal.",
                "JOURNAL_APPEND_BATCH_FAILED",
                ex.Message
            );
        }
    }

    // Builds one journal row with an allocated StreamPosition. The caller owns the transaction so the
    // allocation and the insert commit (or roll back) together.
    private async Task<Result<EventJournal>> AppendOneAsync(
        AppendEventRequest request,
        CancellationToken cancellationToken
    )
    {
        // The platform-global stream (null tenant) keys its sequence on Guid.Empty so a single counter advances
        // for all platform-level events, mirroring the BroadcasterId == Guid.Empty domain-event sentinel.
        Guid sequenceTenant = request.BroadcasterId ?? Guid.Empty;
        Result<long> position = await _sequences.NextAsync(
            sequenceTenant,
            ITenantSequenceAllocator.EventStreamPositionSequence,
            cancellationToken
        );
        if (position.IsFailure)
            return Result.Failure<EventJournal>(
                position.ErrorMessage!,
                position.ErrorCode,
                position.ErrorDetail
            );

        EventJournal entity = BuildJournalEntity(
            request,
            position.Value,
            _clock.GetUtcNow().UtcDateTime
        );
        await _db.EventJournals.AddAsync(entity, cancellationToken);
        return Result.Success(entity);
    }

    // Materializes one journal row from a request at an already-allocated StreamPosition. Shared by the single
    // and batch append paths so the row shape stays identical regardless of how the position was reserved.
    private static EventJournal BuildJournalEntity(
        AppendEventRequest request,
        long position,
        DateTime recordedAt
    ) =>
        new()
        {
            EventId = request.EventId,
            BroadcasterId = request.BroadcasterId,
            StreamPosition = position,
            EventType = request.EventType,
            EventVersion = request.EventVersion,
            Source = request.Source,
            Payload = request.PayloadJson,
            PayloadIsEncrypted = false,
            SubjectKeyId = null,
            CorrelationId = request.CorrelationId,
            CausationId = request.CausationId,
            ActorUserId = request.ActorUserId,
            ActorTwitchUserId = request.ActorTwitchUserId,
            Metadata = request.MetadataJson,
            OccurredAt = DateTime.SpecifyKind(request.OccurredAt, DateTimeKind.Utc),
            RecordedAt = recordedAt,
        };

    // Loads the already-stored journal rows for the given event ids, keyed by EventId, chunking the IN-list so a
    // large batch never exceeds the provider parameter limit. Used by the batch append to dedupe in one pass and
    // to return the stored record for any duplicate (idempotency).
    private async Task<Dictionary<Guid, EventJournal>> LoadStoredByEventIdAsync(
        IReadOnlyList<Guid> eventIds,
        CancellationToken cancellationToken
    )
    {
        Dictionary<Guid, EventJournal> stored = [];
        if (eventIds.Count == 0)
            return stored;

        const int chunkSize = 1000;
        Guid[] ids = [.. eventIds];
        for (int offset = 0; offset < ids.Length; offset += chunkSize)
        {
            Guid[] chunk = ids[offset..Math.Min(offset + chunkSize, ids.Length)];
            List<EventJournal> rows = await _db
                .EventJournals.AsNoTracking()
                .Where(e => chunk.Contains(e.EventId))
                .ToListAsync(cancellationToken);
            foreach (EventJournal row in rows)
                stored[row.EventId] = row;
        }

        return stored;
    }

    public async Task<Result<IReadOnlyList<EventRecord>>> ReadStreamAsync(
        Guid? broadcasterId,
        long afterPosition,
        int limit,
        CancellationToken cancellationToken = default
    )
    {
        List<EventJournal> rows = await _db
            .EventJournals.AsNoTracking()
            .Where(e => e.BroadcasterId == broadcasterId && e.StreamPosition > afterPosition)
            .OrderBy(e => e.StreamPosition)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<EventRecord>>(rows.Select(Map).ToList());
    }

    public async Task<Result<IReadOnlyList<EventRecord>>> ReadAllAsync(
        long afterId,
        int limit,
        CancellationToken cancellationToken = default
    )
    {
        List<EventJournal> rows = await _db
            .EventJournals.AsNoTracking()
            .Where(e => e.Id > afterId)
            .OrderBy(e => e.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<EventRecord>>(rows.Select(Map).ToList());
    }

    public async Task<Result<EventRecord>> GetByEventIdAsync(
        Guid eventId,
        CancellationToken cancellationToken = default
    )
    {
        EventJournal? row = await FindByEventIdAsync(eventId, cancellationToken);
        return row is null
            ? Result.Failure<EventRecord>("Event not found.", "NOT_FOUND")
            : Result.Success(Map(row));
    }

    public async Task<Result<IReadOnlySet<Guid>>> GetExistingEventIdsAsync(
        IReadOnlyCollection<Guid> candidateEventIds,
        CancellationToken cancellationToken = default
    )
    {
        if (candidateEventIds.Count == 0)
            return Result.Success<IReadOnlySet<Guid>>(new HashSet<Guid>());

        // Chunk the IN-list so a very large candidate set never blows the provider's parameter limit; each chunk is
        // one indexed query on EventId. The union is the set of ids already journaled.
        HashSet<Guid> existing = new();
        const int chunkSize = 1000;
        Guid[] candidates = candidateEventIds.ToArray();
        for (int offset = 0; offset < candidates.Length; offset += chunkSize)
        {
            Guid[] chunk = candidates[offset..Math.Min(offset + chunkSize, candidates.Length)];
            List<Guid> found = await _db
                .EventJournals.AsNoTracking()
                .Where(e => chunk.Contains(e.EventId))
                .Select(e => e.EventId)
                .ToListAsync(cancellationToken);
            existing.UnionWith(found);
        }

        return Result.Success<IReadOnlySet<Guid>>(existing);
    }

    public async Task<Result<long>> GetHeadPositionAsync(
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        IQueryable<EventJournal> tenantStream = _db
            .EventJournals.AsNoTracking()
            .Where(e => e.BroadcasterId == broadcasterId);

        long head = await tenantStream.AnyAsync(cancellationToken)
            ? await tenantStream.MaxAsync(e => e.StreamPosition, cancellationToken)
            : 0L;
        return Result.Success(head);
    }

    public async Task<Result<PagedList<EventRecord>>> QueryAsync(
        EventJournalQuery query,
        CancellationToken cancellationToken = default
    )
    {
        IQueryable<EventJournal> filtered = _db
            .EventJournals.AsNoTracking()
            .Where(e =>
                (query.BroadcasterId == null || e.BroadcasterId == query.BroadcasterId)
                && (query.EventType == null || e.EventType == query.EventType)
                && (query.FromUtc == null || e.OccurredAt >= query.FromUtc)
                && (query.ToUtc == null || e.OccurredAt <= query.ToUtc)
                && (query.ActorUserId == null || e.ActorUserId == query.ActorUserId)
            );

        int total = await filtered.CountAsync(cancellationToken);
        List<EventJournal> page = await filtered
            .OrderByDescending(e => e.OccurredAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return Result.Success(
            new PagedList<EventRecord>(page.Select(Map).ToList(), query.Page, query.PageSize, total)
        );
    }

    private Task<EventJournal?> FindByEventIdAsync(
        Guid eventId,
        CancellationToken cancellationToken
    ) =>
        _db
            .EventJournals.AsNoTracking()
            .FirstOrDefaultAsync(e => e.EventId == eventId, cancellationToken);

    // Drops the tracked entity graph so a long batched append stays O(batch), not O(total). Every
    // IApplicationDbContext impl in this app IS a DbContext (the same seam ITenantSequenceAllocator relies on).
    private void ClearChangeTracker()
    {
        if (_db is DbContext context)
            context.ChangeTracker.Clear();
    }

    internal static EventRecord Map(EventJournal e) =>
        new(
            e.Id,
            e.EventId,
            e.BroadcasterId,
            e.StreamPosition,
            e.EventType,
            e.EventVersion,
            e.Source,
            e.Payload,
            e.PayloadIsEncrypted,
            e.SubjectKeyId,
            e.CorrelationId,
            e.CausationId,
            e.ActorUserId,
            e.ActorTwitchUserId,
            e.Metadata,
            e.OccurredAt,
            e.RecordedAt
        );
}

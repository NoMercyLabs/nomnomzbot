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

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            List<EventRecord> results = [];
            foreach (AppendEventRequest request in requests)
            {
                EventJournal? existing = await FindByEventIdAsync(
                    request.EventId,
                    cancellationToken
                );
                if (existing is not null)
                {
                    results.Add(Map(existing));
                    continue;
                }

                Result<EventJournal> appended = await AppendOneAsync(request, cancellationToken);
                if (appended.IsFailure)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return Result.Failure<IReadOnlyList<EventRecord>>(
                        appended.ErrorMessage!,
                        appended.ErrorCode,
                        appended.ErrorDetail
                    );
                }

                // Flush each row so the next dedupe lookup within the batch sees in-batch duplicates.
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                results.Add(Map(appended.Value));
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
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

        DateTime now = _clock.GetUtcNow().UtcDateTime;
        EventJournal entity = new()
        {
            EventId = request.EventId,
            BroadcasterId = request.BroadcasterId,
            StreamPosition = position.Value,
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
            RecordedAt = now,
        };

        await _db.EventJournals.AddAsync(entity, cancellationToken);
        return Result.Success(entity);
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

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
/// Per-tenant monotonic sequence allocator (schema Q.3). Reads-and-increments the
/// <see cref="TenantSequence.NextValue"/> for <c>(BroadcasterId, SequenceName)</c> in the caller's ambient
/// transaction, so the allocation commits atomically with the consuming insert. The unique
/// <c>(BroadcasterId, SequenceName)</c> constraint serializes concurrent allocators for the same tenant — the
/// loser of a race fails the unique insert and retries, never handing out a duplicate position. On a
/// relational Postgres provider the held row is additionally pinned with <c>SELECT … FOR UPDATE</c>; on SQLite
/// the write transaction (<c>BEGIN IMMEDIATE</c>) already excludes concurrent writers, so no extra hint is
/// needed. Different tenants touch different rows and never contend, giving each tenant an independent stream.
/// </summary>
public sealed class TenantSequenceAllocator : ITenantSequenceAllocator
{
    private readonly IApplicationDbContext _db;
    private readonly DbContext _dbContext;

    public TenantSequenceAllocator(IApplicationDbContext db)
    {
        _db = db;
        // The allocator needs the relational seam (provider check + row-lock SQL) that only the concrete
        // DbContext exposes. Every IApplicationDbContext impl in this app IS a DbContext.
        _dbContext =
            db as DbContext
            ?? throw new InvalidOperationException(
                "ITenantSequenceAllocator requires a relational DbContext-backed IApplicationDbContext."
            );
    }

    public Task<Result<long>> NextAsync(
        Guid broadcasterId,
        string sequenceName,
        CancellationToken cancellationToken = default
    ) => AllocateAsync(broadcasterId, sequenceName, 1, cancellationToken);

    public async Task<Result<long>> NextBlockAsync(
        Guid broadcasterId,
        string sequenceName,
        int count,
        CancellationToken cancellationToken = default
    )
    {
        if (count < 1)
            return Result.Failure<long>("Block size must be at least 1.", "INVALID_SEQUENCE_BLOCK");

        return await AllocateAsync(broadcasterId, sequenceName, count, cancellationToken);
    }

    private async Task<Result<long>> AllocateAsync(
        Guid broadcasterId,
        string sequenceName,
        int count,
        CancellationToken cancellationToken
    )
    {
        TenantSequence? sequence = await LoadLockedAsync(
            broadcasterId,
            sequenceName,
            cancellationToken
        );

        if (sequence is null)
        {
            // First allocation for this tenant/sequence: start the stream at 1. A racing creator loses the
            // unique (BroadcasterId, SequenceName) insert; the caller's IUnitOfWork surfaces that and retries.
            sequence = new TenantSequence
            {
                BroadcasterId = broadcasterId,
                SequenceName = sequenceName,
                NextValue = 1,
                UpdatedAt = DateTime.UtcNow,
            };
            await _db.TenantSequences.AddAsync(sequence, cancellationToken);
        }

        long first = sequence.NextValue;
        sequence.NextValue = first + count;
        sequence.UpdatedAt = DateTime.UtcNow;

        // Flush the increment so the new NextValue is visible within this transaction before the consuming
        // insert runs. The outer IUnitOfWork owns the commit/rollback.
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(first);
    }

    private async Task<TenantSequence?> LoadLockedAsync(
        Guid broadcasterId,
        string sequenceName,
        CancellationToken cancellationToken
    )
    {
        if (_dbContext.Database.IsNpgsql())
        {
            // Pin the row for the duration of the ambient transaction so a concurrent allocator for the same
            // tenant blocks here rather than reading a stale NextValue.
            return await _db
                .TenantSequences.FromSqlInterpolated(
                    $"SELECT * FROM \"TenantSequences\" WHERE \"BroadcasterId\" = {broadcasterId} AND \"SequenceName\" = {sequenceName} FOR UPDATE"
                )
                .AsTracking()
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await _db.TenantSequences.FirstOrDefaultAsync(
            s => s.BroadcasterId == broadcasterId && s.SequenceName == sequenceName,
            cancellationToken
        );
    }
}

// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;

namespace NomNomzBot.Infrastructure.Commands;

/// <summary>
/// <see cref="INamedCounterService"/> over the G.4 <see cref="NamedCounter"/> table. Adjust is a
/// read-modify-write under an optimistic concurrency token on <c>Value</c> with bounded retries, so
/// concurrent increments always sum instead of overwriting each other.
/// </summary>
public sealed partial class NamedCounterService : INamedCounterService
{
    private const int MaxWriteAttempts = 5;

    private readonly IApplicationDbContext _db;

    public NamedCounterService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Result> SetAsync(
        Guid broadcasterId,
        string key,
        long value,
        CancellationToken ct = default
    )
    {
        Result<string> normalized = NormalizeKey(key);
        if (normalized.IsFailure)
            return Result.Failure(normalized.ErrorMessage!, normalized.ErrorCode);

        for (int attempt = 0; attempt < MaxWriteAttempts; attempt++)
        {
            NamedCounter? row = await FindLiveRowAsync(broadcasterId, normalized.Value, ct);
            if (row is null)
                await _db.NamedCounters.AddAsync(
                    new NamedCounter
                    {
                        BroadcasterId = broadcasterId,
                        Key = normalized.Value,
                        Value = value,
                    },
                    ct
                );
            else
                row.Value = value;

            try
            {
                await _db.SaveChangesAsync(ct);
                return Result.Success();
            }
            catch (DbUpdateException ex)
            {
                DetachFailedEntries(ex);
            }
        }

        return Result.Failure(
            "The counter could not be updated under contention — try again.",
            "CONFLICT"
        );
    }

    public async Task<Result<long>> AdjustAsync(
        Guid broadcasterId,
        string key,
        long delta,
        CancellationToken ct = default
    )
    {
        Result<string> normalized = NormalizeKey(key);
        if (normalized.IsFailure)
            return Result.Failure<long>(normalized.ErrorMessage!, normalized.ErrorCode);

        for (int attempt = 0; attempt < MaxWriteAttempts; attempt++)
        {
            NamedCounter? row = await FindLiveRowAsync(broadcasterId, normalized.Value, ct);
            long next;
            if (row is null)
            {
                next = delta;
                await _db.NamedCounters.AddAsync(
                    new NamedCounter
                    {
                        BroadcasterId = broadcasterId,
                        Key = normalized.Value,
                        Value = next,
                    },
                    ct
                );
            }
            else
            {
                next = row.Value + delta;
                row.Value = next;
            }

            try
            {
                await _db.SaveChangesAsync(ct);
                return Result.Success(next);
            }
            catch (DbUpdateException ex)
            {
                // Lost a race (unique-insert twin or a concurrent increment) — detach and re-read.
                DetachFailedEntries(ex);
            }
        }

        return Result.Failure<long>(
            "The counter could not be updated under contention — try again.",
            "CONFLICT"
        );
    }

    public async Task<Result<IReadOnlyDictionary<string, long>>> LoadKeysAsync(
        Guid broadcasterId,
        IReadOnlyCollection<string> keys,
        CancellationToken ct = default
    )
    {
        if (keys.Count == 0)
            return Result.Success<IReadOnlyDictionary<string, long>>(
                new Dictionary<string, long>()
            );

        List<string> wanted = keys.Select(k => k.Trim().ToLowerInvariant()).Distinct().ToList();
        Dictionary<string, long> map = await _db
            .NamedCounters.AsNoTracking()
            .Where(c => c.BroadcasterId == broadcasterId && wanted.Contains(c.Key))
            .ToDictionaryAsync(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase, ct);
        return Result.Success<IReadOnlyDictionary<string, long>>(map);
    }

    // ─── Internals ────────────────────────────────────────────────────────────

    private Task<NamedCounter?> FindLiveRowAsync(
        Guid broadcasterId,
        string key,
        CancellationToken ct
    ) =>
        _db.NamedCounters.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId && c.Key == key,
            ct
        );

    private static void DetachFailedEntries(DbUpdateException ex)
    {
        foreach (Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry in ex.Entries)
            entry.State = EntityState.Detached;
    }

    private static Result<string> NormalizeKey(string key)
    {
        string slug = key.Trim().ToLowerInvariant();
        return KeyPattern().IsMatch(slug)
            ? Result.Success(slug)
            : Result.Failure<string>(
                "Counter keys are 1-50 character slugs: lowercase letters, digits, '_' or '-'.",
                "VALIDATION_FAILED"
            );
    }

    [GeneratedRegex("^[a-z0-9_-]{1,50}$")]
    private static partial Regex KeyPattern();
}

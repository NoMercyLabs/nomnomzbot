// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.ViewerData.Services;
using NomNomzBot.Domain.ViewerData.Entities;

namespace NomNomzBot.Infrastructure.ViewerData;

/// <summary>
/// <see cref="IViewerDataService"/> (per-viewer-data.md §3). Keys normalize to lowercase slugs; writes are
/// bounded (D5: value ≤ 500 chars, ≤ 100 live keys per viewer per channel — over-cap is rejected, never
/// truncated). <see cref="AdjustAsync"/> is a read-modify-write under an optimistic concurrency token on
/// <c>Value</c> with bounded retries, so concurrent increments always sum instead of overwriting each other.
/// </summary>
public sealed partial class ViewerDataService : IViewerDataService
{
    /// <summary>Safety baseline (D5): live keys per viewer per channel.</summary>
    internal const int MaxKeysPerViewer = 100;

    /// <summary>Safety baseline (D5): value length in characters.</summary>
    internal const int MaxValueLength = 500;

    private const int MaxWriteAttempts = 5;

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _clock;

    public ViewerDataService(IApplicationDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<string?>> GetAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        string key,
        CancellationToken ct = default
    )
    {
        Result<string> normalized = NormalizeKey(key);
        if (normalized.IsFailure)
            return Result.Failure<string?>(normalized.ErrorMessage!, normalized.ErrorCode);

        string? value = await _db
            .ViewerData.AsNoTracking()
            .Where(d =>
                d.BroadcasterId == broadcasterId
                && d.ViewerUserId == viewerUserId
                && d.Key == normalized.Value
            )
            .Select(d => d.Value)
            .FirstOrDefaultAsync(ct);
        return Result.Success(value);
    }

    public async Task<Result<IReadOnlyDictionary<string, string>>> ListForViewerAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        CancellationToken ct = default
    )
    {
        Dictionary<string, string> map = await _db
            .ViewerData.AsNoTracking()
            .Where(d => d.BroadcasterId == broadcasterId && d.ViewerUserId == viewerUserId)
            .OrderBy(d => d.Key)
            .ToDictionaryAsync(d => d.Key, d => d.Value, StringComparer.OrdinalIgnoreCase, ct);
        return Result.Success<IReadOnlyDictionary<string, string>>(map);
    }

    public async Task<Result> SetAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        string key,
        string value,
        CancellationToken ct = default
    )
    {
        Result<string> normalized = NormalizeKey(key);
        if (normalized.IsFailure)
            return Result.Failure(normalized.ErrorMessage!, normalized.ErrorCode);
        if (value.Length > MaxValueLength)
            return Result.Failure(
                $"The value exceeds the {MaxValueLength}-character limit.",
                "VALIDATION_FAILED"
            );

        for (int attempt = 0; attempt < MaxWriteAttempts; attempt++)
        {
            ViewerDatum? row = await FindLiveRowAsync(
                broadcasterId,
                viewerUserId,
                normalized.Value,
                ct
            );
            if (row is null)
            {
                Result admissible = await AdmitNewKeyAsync(broadcasterId, viewerUserId, ct);
                if (admissible.IsFailure)
                    return admissible;
                row = NewRow(broadcasterId, viewerUserId, normalized.Value, value);
                await _db.ViewerData.AddAsync(row, ct);
            }
            else
            {
                row.Value = value;
            }

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

        return ContentionFailure();
    }

    public async Task<Result<long>> AdjustAsync(
        Guid broadcasterId,
        Guid viewerUserId,
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
            ViewerDatum? row = await FindLiveRowAsync(
                broadcasterId,
                viewerUserId,
                normalized.Value,
                ct
            );
            long next;
            if (row is null)
            {
                Result admissible = await AdmitNewKeyAsync(broadcasterId, viewerUserId, ct);
                if (admissible.IsFailure)
                    return Result.Failure<long>(admissible.ErrorMessage!, admissible.ErrorCode);
                next = delta;
                row = NewRow(
                    broadcasterId,
                    viewerUserId,
                    normalized.Value,
                    next.ToString(CultureInfo.InvariantCulture)
                );
                await _db.ViewerData.AddAsync(row, ct);
            }
            else
            {
                if (
                    !long.TryParse(
                        row.Value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out long current
                    )
                )
                    return Result.Failure<long>(
                        $"'{normalized.Value}' holds a non-numeric value and cannot be adjusted.",
                        "VALIDATION_FAILED"
                    );
                next = current + delta;
                row.Value = next.ToString(CultureInfo.InvariantCulture);
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
            "The value could not be updated under contention — try again.",
            "CONFLICT"
        );
    }

    public async Task<Result> DeleteAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        string key,
        CancellationToken ct = default
    )
    {
        Result<string> normalized = NormalizeKey(key);
        if (normalized.IsFailure)
            return Result.Failure(normalized.ErrorMessage!, normalized.ErrorCode);

        ViewerDatum? row = await FindLiveRowAsync(
            broadcasterId,
            viewerUserId,
            normalized.Value,
            ct
        );
        if (row is null)
            return Result.Failure($"'{normalized.Value}' is not set for this viewer.", "NOT_FOUND");

        row.DeletedAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyDictionary<string, string>>> LoadKeysAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        IReadOnlyCollection<string> keys,
        CancellationToken ct = default
    )
    {
        if (keys.Count == 0)
            return Result.Success<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>()
            );

        List<string> wanted = keys.Select(k => k.Trim().ToLowerInvariant()).Distinct().ToList();
        Dictionary<string, string> map = await _db
            .ViewerData.AsNoTracking()
            .Where(d =>
                d.BroadcasterId == broadcasterId
                && d.ViewerUserId == viewerUserId
                && wanted.Contains(d.Key)
            )
            .ToDictionaryAsync(d => d.Key, d => d.Value, StringComparer.OrdinalIgnoreCase, ct);
        return Result.Success<IReadOnlyDictionary<string, string>>(map);
    }

    // ─── Internals ────────────────────────────────────────────────────────────

    private Task<ViewerDatum?> FindLiveRowAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        string key,
        CancellationToken ct
    ) =>
        _db.ViewerData.FirstOrDefaultAsync(
            d => d.BroadcasterId == broadcasterId && d.ViewerUserId == viewerUserId && d.Key == key,
            ct
        );

    /// <summary>New-key admission: the viewer must exist and stay under the per-viewer key cap (D5).</summary>
    private async Task<Result> AdmitNewKeyAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        CancellationToken ct
    )
    {
        bool viewerExists = await _db.Users.AnyAsync(u => u.Id == viewerUserId, ct);
        if (!viewerExists)
            return Result.Failure("The target viewer does not exist.", "NOT_FOUND");

        int liveKeys = await _db.ViewerData.CountAsync(
            d => d.BroadcasterId == broadcasterId && d.ViewerUserId == viewerUserId,
            ct
        );
        return liveKeys >= MaxKeysPerViewer
            ? Result.Failure(
                $"This viewer already has {MaxKeysPerViewer} data keys — delete one first.",
                "VALIDATION_FAILED"
            )
            : Result.Success();
    }

    private static ViewerDatum NewRow(
        Guid broadcasterId,
        Guid viewerUserId,
        string key,
        string value
    ) =>
        new()
        {
            BroadcasterId = broadcasterId,
            ViewerUserId = viewerUserId,
            Key = key,
            Value = value,
        };

    private static void DetachFailedEntries(DbUpdateException ex)
    {
        foreach (Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry in ex.Entries)
            entry.State = EntityState.Detached;
    }

    private static Result ContentionFailure() =>
        Result.Failure("The value could not be updated under contention — try again.", "CONFLICT");

    private static Result<string> NormalizeKey(string key)
    {
        string slug = key.Trim().ToLowerInvariant();
        return KeyPattern().IsMatch(slug)
            ? Result.Success(slug)
            : Result.Failure<string>(
                "Keys are 1-50 character slugs: lowercase letters, digits, '_' or '-'.",
                "VALIDATION_FAILED"
            );
    }

    [GeneratedRegex("^[a-z0-9_-]{1,50}$")]
    private static partial Regex KeyPattern();
}

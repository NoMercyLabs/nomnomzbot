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
using NomNomzBot.Domain.Music.Entities;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// The durable side of the song-request fair queue (S001b). Every accepted queue mutation
/// (<see cref="MusicService"/>'s enqueue/rollback/remove, <see cref="SongRequestQueueReconciler"/>'s
/// drain) calls <see cref="SyncAsync"/> immediately afterward with the queue's fresh in-memory
/// snapshot — write-through, not a debounced or batched write, so a hard kill immediately after the
/// call that mutated the in-memory <c>FairQueue</c> still has the row committed to disk: nothing is
/// ever "confirmed" to the caller (a chat reply, a dashboard 200) before its persisted counterpart is
/// on disk. The write itself replaces one channel's whole row set inside one transaction (delete-then-
/// insert) rather than diffing — the fair queue is typically tens of entries, and matching the DB
/// shape to the in-memory shape (a full ordered list) is far simpler to keep provably correct than
/// tracking per-row deltas across five different mutation shapes (enqueue, rollback, RemoveAt,
/// RemoveFirst, RemoveThrough). The transaction is what keeps a kill mid-write from leaving the table
/// briefly empty: readers only ever see the old complete set or the new complete set, never neither.
/// </summary>
public interface ISongRequestQueuePersistence
{
    /// <summary>Replaces the persisted row set for <paramref name="broadcasterId"/> with the given
    /// snapshot, atomically. An empty snapshot persists as "no rows" (a fully-drained queue).
    /// <paramref name="inFlight"/> — when given, the entry already handed to the provider (matched by
    /// reference against <paramref name="snapshot"/>'s items) — is stamped onto its row so a restart can
    /// tell that entry apart from one merely queued (S-SR-INFLIGHT-DURABLE).</summary>
    Task SyncAsync(
        string broadcasterId,
        IReadOnlyList<(SongRequestEntry Item, int Rank, string OwnerKey)> snapshot,
        CancellationToken cancellationToken,
        SongRequestEntry? inFlight = null
    );

    /// <summary>
    /// Loads every channel's persisted queue for startup restore. A channel whose rows are all older
    /// than <paramref name="freshnessWindow"/> is treated as unrestorable — a queue frozen since before
    /// the bot's last graceful/crash window is worse than an empty one (viewers would see stale
    /// requests resurrect from a stream days ago) — its rows are purged and it comes back in
    /// <see cref="SongRequestQueueRestoreResult.DiscardedStaleBroadcasterIds"/> instead of
    /// <see cref="SongRequestQueueRestoreResult.Channels"/>.
    /// </summary>
    Task<SongRequestQueueRestoreResult> LoadForRestoreAsync(
        TimeSpan freshnessWindow,
        CancellationToken cancellationToken
    );
}

/// <summary>One channel's restorable queue, in the exact insertion order it needs to be replayed
/// through <c>FairQueue.Enqueue</c> to reproduce the original rank state. <see cref="InFlightIndex"/>,
/// when set, is the position within <see cref="OrderedEntries"/> of the entry already handed to the
/// provider before the restart — the caller replays it through the SAME object reference so
/// <c>SongRequestQueueStore.GetInFlight</c> and the reconciler's reference-equality check against the
/// live queue keep working after restore.</summary>
public sealed record RestoredSongRequestQueue(
    string BroadcasterId,
    IReadOnlyList<(string OwnerKey, SongRequestEntry Entry)> OrderedEntries,
    int? InFlightIndex
);

public sealed record SongRequestQueueRestoreResult(
    IReadOnlyList<RestoredSongRequestQueue> Channels,
    IReadOnlyList<string> DiscardedStaleBroadcasterIds
);

/// <inheritdoc cref="ISongRequestQueuePersistence"/>
public sealed class SongRequestQueuePersistence : ISongRequestQueuePersistence
{
    private readonly AppDbContext _db;

    public SongRequestQueuePersistence(AppDbContext db) => _db = db;

    public Task SyncAsync(
        string broadcasterId,
        IReadOnlyList<(SongRequestEntry Item, int Rank, string OwnerKey)> snapshot,
        CancellationToken cancellationToken,
        SongRequestEntry? inFlight = null
    ) =>
        // Retriable unit — a bare Begin/Commit is rejected outright by Npgsql's retrying execution
        // strategy. Delete-then-insert of one channel's whole row set is idempotent, so a retried
        // attempt reproduces exactly the same end state.
        _db.ExecuteInTransactionAsync(
            async token => await WriteSnapshotAsync(broadcasterId, snapshot, inFlight, token),
            cancellationToken
        );

    private async Task WriteSnapshotAsync(
        string broadcasterId,
        IReadOnlyList<(SongRequestEntry Item, int Rank, string OwnerKey)> snapshot,
        SongRequestEntry? inFlight,
        CancellationToken cancellationToken
    )
    {
        await _db
            .SongRequestQueueItems.Where(r => r.BroadcasterId == broadcasterId)
            .ExecuteDeleteAsync(cancellationToken);

        if (snapshot.Count > 0)
        {
            DateTime now = DateTime.UtcNow;
            List<SongRequestQueueItem> rows =
            [
                .. snapshot.Select(
                    (entry, index) =>
                        new SongRequestQueueItem
                        {
                            BroadcasterId = broadcasterId,
                            Sequence = index,
                            OwnerKey = entry.OwnerKey,
                            TrackUri = entry.Item.TrackUri,
                            TrackName = entry.Item.TrackName,
                            Artist = entry.Item.Artist,
                            ImageUrl = entry.Item.ImageUrl,
                            DurationMs = entry.Item.DurationMs,
                            CreatedAt = now,
                            IsInFlight =
                                inFlight is not null && ReferenceEquals(inFlight, entry.Item),
                            Code = entry.Item.Code,
                        }
                ),
            ];

            _db.SongRequestQueueItems.AddRange(rows);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<SongRequestQueueRestoreResult> LoadForRestoreAsync(
        TimeSpan freshnessWindow,
        CancellationToken cancellationToken
    )
    {
        List<SongRequestQueueItem> rows = await _db
            .SongRequestQueueItems.AsNoTracking()
            .OrderBy(r => r.BroadcasterId)
            .ThenBy(r => r.Sequence)
            .ToListAsync(cancellationToken);

        DateTime cutoff = DateTime.UtcNow - freshnessWindow;
        List<RestoredSongRequestQueue> restored = [];
        List<string> staleBroadcasterIds = [];

        foreach (
            IGrouping<string, SongRequestQueueItem> channel in rows.GroupBy(r => r.BroadcasterId)
        )
        {
            // Every row for a channel shares the same CreatedAt (SyncAsync stamps the whole set on
            // every write), so "oldest row" is really "when this channel's queue was last touched" —
            // exactly the staleness question: how long has the bot been down since then.
            DateTime lastTouched = channel.Max(r => r.CreatedAt);
            if (lastTouched < cutoff)
            {
                staleBroadcasterIds.Add(channel.Key);
                continue;
            }

            List<SongRequestQueueItem> orderedRows = [.. channel.OrderBy(r => r.Sequence)];
            int inFlightIndex = orderedRows.FindIndex(r => r.IsInFlight);

            // Persisted codes are unique per channel by construction (SyncAsync writes exactly one row
            // per live queue entry, and MusicService.NextSongCode never hands out a code already in the
            // live queue) — the migration that added this column backfilled every pre-existing row the
            // same way. taken/reassignment here is a safety net, not the normal path: a row that somehow
            // still carries no code (or, impossibly, a duplicate) gets a fresh one rather than coming
            // back unusable to every code-addressed command.
            HashSet<string> takenCodes = new(StringComparer.Ordinal);
            List<(string OwnerKey, SongRequestEntry Entry)> orderedEntries = new(orderedRows.Count);
            foreach (SongRequestQueueItem r in orderedRows)
            {
                string code = r.Code;
                if (string.IsNullOrEmpty(code) || !takenCodes.Add(code))
                {
                    code = SongCode.NextAvailable(takenCodes) ?? string.Empty;
                    if (!string.IsNullOrEmpty(code))
                        takenCodes.Add(code);
                }

                orderedEntries.Add(
                    (
                        r.OwnerKey,
                        new SongRequestEntry(
                            r.TrackUri,
                            r.TrackName,
                            r.Artist,
                            r.ImageUrl,
                            r.DurationMs,
                            r.OwnerKey,
                            r.Cost,
                            r.RequesterUserId,
                            code
                        )
                    )
                );
            }

            restored.Add(
                new(channel.Key, orderedEntries, inFlightIndex < 0 ? null : inFlightIndex)
            );
        }

        if (staleBroadcasterIds.Count > 0)
        {
            await _db
                .SongRequestQueueItems.Where(r => staleBroadcasterIds.Contains(r.BroadcasterId))
                .ExecuteDeleteAsync(cancellationToken);
        }

        return new(restored, staleBroadcasterIds);
    }
}

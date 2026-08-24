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
    /// snapshot, atomically. An empty snapshot persists as "no rows" (a fully-drained queue).</summary>
    Task SyncAsync(
        string broadcasterId,
        IReadOnlyList<(SongRequestEntry Item, int Rank, string OwnerKey)> snapshot,
        CancellationToken cancellationToken
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
/// through <c>FairQueue.Enqueue</c> to reproduce the original rank state.</summary>
public sealed record RestoredSongRequestQueue(
    string BroadcasterId,
    IReadOnlyList<(string OwnerKey, SongRequestEntry Entry)> OrderedEntries
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
        CancellationToken cancellationToken
    ) =>
        // Retriable unit — a bare Begin/Commit is rejected outright by Npgsql's retrying execution
        // strategy. Delete-then-insert of one channel's whole row set is idempotent, so a retried
        // attempt reproduces exactly the same end state.
        _db.ExecuteInTransactionAsync(
            async token => await WriteSnapshotAsync(broadcasterId, snapshot, token),
            cancellationToken
        );

    private async Task WriteSnapshotAsync(
        string broadcasterId,
        IReadOnlyList<(SongRequestEntry Item, int Rank, string OwnerKey)> snapshot,
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

            restored.Add(
                new(
                    channel.Key,
                    [
                        .. channel
                            .OrderBy(r => r.Sequence)
                            .Select(r =>
                                (
                                    r.OwnerKey,
                                    new SongRequestEntry(
                                        r.TrackUri,
                                        r.TrackName,
                                        r.Artist,
                                        r.ImageUrl,
                                        r.DurationMs,
                                        r.OwnerKey
                                    )
                                )
                            ),
                    ]
                )
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

// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// The process-wide home for every channel's song-request fair queue. <see cref="MusicService"/> is
/// registered scoped (one instance per HTTP request / chat-command dispatch), so a queue field on
/// MusicService itself resets every time — a viewer's `!sr` would appear to enqueue, then `!queue`
/// and `GET /queue` on the NEXT scope would see an empty queue again. This store is the single
/// singleton the DI container hands to every scope, so all of them observe the same live queues.
/// <para>
/// Keyed by the raw <c>broadcasterId</c> string channel-id (never cross-tenant: each key is one
/// channel's queue, and a lookup by another channel's id can never return this one's entries).
/// <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey,System.Func{TKey,TValue})"/> makes queue
/// creation atomic per tenant, and every mutation on the returned <see cref="FairQueue{T}"/> instance
/// is itself internally lock-protected (add/dequeue/remove/snapshot), so concurrent viewers hitting
/// the same channel never race each other or lose an entry.
/// </para>
/// <para>
/// The queues here are the live, in-memory serving path — every read/write in the hot `!sr` / `!queue`
/// path only ever touches this store, never the database. Durability (S001b) is layered on top, not
/// baked in here: <see cref="MusicService"/> and <see cref="SongRequestQueueReconciler"/> call
/// <c>ISongRequestQueuePersistence.SyncAsync</c> immediately after every mutation they make to a
/// queue returned from here, and <c>SongRequestQueueRestoreHostedService</c> calls <see cref="Restore"/>
/// once at startup, before any live traffic, to replay a fresh persisted queue back into an empty one.
/// </para>
/// </summary>
/// <summary>
/// Hands the next pending song request to the music provider. Split out from <c>IMusicService</c> because
/// only <see cref="SongRequestQueueReconciler"/> needs it: the reconciler knows WHEN the provider is free
/// (it watches live playback), while <c>MusicService</c> knows HOW to reach the provider for a tenant.
/// </summary>
public interface ISongRequestHandover
{
    /// <summary>Pops the fair queue's head, pushes it to the provider, and records it as in-flight.
    /// Does nothing when the queue is empty or the provider is unreachable — a request that could not be
    /// handed over stays at the head of the queue for the next attempt rather than being dropped.</summary>
    Task HandOverNextAsync(string broadcasterId, CancellationToken cancellationToken = default);
}

public interface ISongRequestQueueStore
{
    /// <summary>Returns the channel's queue, creating an empty one atomically if none exists yet.</summary>
    FairQueue<SongRequestEntry> GetOrCreate(string broadcasterId);

    /// <summary>Returns the channel's queue if one has ever been created; null otherwise (never creates one).</summary>
    FairQueue<SongRequestEntry>? TryGet(string broadcasterId);

    /// <summary>
    /// The one request this channel has handed to the provider and is waiting on — null when the
    /// provider holds nothing of ours. The fair queue is the authority on ORDER, so only a single track
    /// is ever pushed ahead: everything behind it stays in our queue where a later request can still be
    /// re-ranked ahead of it. Pushing the whole queue to the provider would freeze that order the moment
    /// each request arrived and make the fair queue decorative.
    /// </summary>
    SongRequestEntry? GetInFlight(string broadcasterId);

    /// <summary>Records (or clears, with null) the request currently handed to the provider.</summary>
    void SetInFlight(string broadcasterId, SongRequestEntry? entry);

    /// <summary>
    /// Replays a persisted queue back into memory at startup (S001b), in the exact order it was
    /// persisted. Only meant to be called once per channel, before any live traffic reaches it —
    /// <see cref="FairQueue{T}.Enqueue"/> derives rank purely from insertion order, so replaying the
    /// same ordered (ownerKey, item) sequence reproduces the exact same rank state the queue had
    /// before the restart.
    /// </summary>
    void Restore(
        string broadcasterId,
        IReadOnlyList<(string OwnerKey, SongRequestEntry Entry)> orderedEntries
    );
}

/// <inheritdoc cref="ISongRequestQueueStore"/>
public sealed class SongRequestQueueStore : ISongRequestQueueStore
{
    private readonly ConcurrentDictionary<string, FairQueue<SongRequestEntry>> _queues = new();
    private readonly ConcurrentDictionary<string, SongRequestEntry> _inFlight = new();

    public FairQueue<SongRequestEntry> GetOrCreate(string broadcasterId) =>
        _queues.GetOrAdd(broadcasterId, static _ => new());

    public FairQueue<SongRequestEntry>? TryGet(string broadcasterId) =>
        _queues.TryGetValue(broadcasterId, out FairQueue<SongRequestEntry>? queue) ? queue : null;

    public SongRequestEntry? GetInFlight(string broadcasterId) =>
        _inFlight.TryGetValue(broadcasterId, out SongRequestEntry? entry) ? entry : null;

    public void SetInFlight(string broadcasterId, SongRequestEntry? entry)
    {
        if (entry is null)
            _inFlight.TryRemove(broadcasterId, out _);
        else
            _inFlight[broadcasterId] = entry;
    }

    public void Restore(
        string broadcasterId,
        IReadOnlyList<(string OwnerKey, SongRequestEntry Entry)> orderedEntries
    )
    {
        FairQueue<SongRequestEntry> queue = GetOrCreate(broadcasterId);
        foreach ((string ownerKey, SongRequestEntry entry) in orderedEntries)
            queue.Enqueue(ownerKey, entry);
    }
}

/// <summary>An item in the per-channel song request queue.</summary>
public sealed record SongRequestEntry(
    string TrackUri,
    string TrackName,
    string Artist,
    string? ImageUrl,
    int DurationMs,
    string RequestedBy
);

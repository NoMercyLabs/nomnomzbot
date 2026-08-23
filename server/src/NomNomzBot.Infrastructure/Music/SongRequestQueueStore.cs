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
/// In-memory only, by design for this slice: the song-request queue is transient live-show state
/// (today's stream's pending requests), not a durable record — restarting the bot mid-stream loses
/// the in-flight queue, same as before this fix. If that turns out to matter (e.g. a crash losing a
/// long queue), the fix is a durable-backed store behind this same interface, not a silent
/// workaround here.
/// </para>
/// </summary>
public interface ISongRequestQueueStore
{
    /// <summary>Returns the channel's queue, creating an empty one atomically if none exists yet.</summary>
    FairQueue<SongRequestEntry> GetOrCreate(string broadcasterId);

    /// <summary>Returns the channel's queue if one has ever been created; null otherwise (never creates one).</summary>
    FairQueue<SongRequestEntry>? TryGet(string broadcasterId);
}

/// <inheritdoc cref="ISongRequestQueueStore"/>
public sealed class SongRequestQueueStore : ISongRequestQueueStore
{
    private readonly ConcurrentDictionary<string, FairQueue<SongRequestEntry>> _queues = new();

    public FairQueue<SongRequestEntry> GetOrCreate(string broadcasterId) =>
        _queues.GetOrAdd(broadcasterId, static _ => new FairQueue<SongRequestEntry>());

    public FairQueue<SongRequestEntry>? TryGet(string broadcasterId) =>
        _queues.TryGetValue(broadcasterId, out FairQueue<SongRequestEntry>? queue) ? queue : null;
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

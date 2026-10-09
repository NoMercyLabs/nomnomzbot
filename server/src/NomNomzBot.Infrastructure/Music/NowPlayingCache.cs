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
using NomNomzBot.Domain.Music.Interfaces;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>The last real read of a channel's track from its music provider, and when it was taken.</summary>
public readonly record struct NowPlayingSnapshot(TrackInfo Track, DateTimeOffset ObservedAt);

/// <summary>
/// The last known <see cref="TrackInfo"/> per channel, kept warm by <see cref="MusicService.GetNowPlayingAsync"/>
/// on every real provider read (the poller ticks every 1s, so an actively streaming channel keeps this fresh
/// without any dedicated writer). Exists so a mutation with a KNOWN outcome (pausing makes IsPlaying false;
/// resuming makes it true — the track itself doesn't change) can publish its state-changed event immediately,
/// without a second round trip to the provider just to re-confirm something already known — see
/// <see cref="MusicService"/>'s pause/play publish path. A provider re-fetch right after issuing the command
/// would also race Spotify's own playback-state propagation delay (a GET immediately after a PUT can still
/// report the pre-mutation state), which this sidesteps entirely by never asking.
/// </summary>
public interface INowPlayingCache
{
    void Set(Guid broadcasterId, TrackInfo track, DateTimeOffset observedAt);

    /// <summary>The cached snapshot, or null if there is none or it is older than <paramref name="maxAge"/> —
    /// callers decide their own staleness tolerance rather than this cache guessing one.</summary>
    NowPlayingSnapshot? TryGet(Guid broadcasterId, TimeSpan maxAge);

    /// <summary>Records that the provider just answered "nothing playing" (or gave no answer).</summary>
    void SetEmpty(Guid broadcasterId, DateTimeOffset observedAt);

    /// <summary>Whether the provider answered "nothing playing" no longer than <paramref name="maxAge"/> ago.</summary>
    bool IsKnownEmpty(Guid broadcasterId, TimeSpan maxAge);
}

public sealed class NowPlayingCache : INowPlayingCache
{
    private readonly ConcurrentDictionary<Guid, NowPlayingSnapshot> _state = new();

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _empty = new();

    public void Set(Guid broadcasterId, TrackInfo track, DateTimeOffset observedAt)
    {
        _state[broadcasterId] = new NowPlayingSnapshot(track, observedAt);
        _empty.TryRemove(broadcasterId, out _);
    }

    public void SetEmpty(Guid broadcasterId, DateTimeOffset observedAt) =>
        _empty[broadcasterId] = observedAt;

    public bool IsKnownEmpty(Guid broadcasterId, TimeSpan maxAge) =>
        _empty.TryGetValue(broadcasterId, out DateTimeOffset at)
        && DateTimeOffset.UtcNow - at <= maxAge;

    public NowPlayingSnapshot? TryGet(Guid broadcasterId, TimeSpan maxAge)
    {
        if (!_state.TryGetValue(broadcasterId, out NowPlayingSnapshot snapshot))
            return null;
        return DateTimeOffset.UtcNow - snapshot.ObservedAt <= maxAge ? snapshot : null;
    }
}

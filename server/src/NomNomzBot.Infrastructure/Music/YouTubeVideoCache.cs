// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using NomNomzBot.Domain.Music.Interfaces;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Recently read, playable YouTube videos by id. A request resolves its video once; the hand-over to the
/// overlay player then describes the same video again, and without this each request spent two
/// videos.list calls from the deployment's daily quota.
/// </summary>
public interface IYouTubeVideoCache
{
    TrackInfo? Get(string videoId);

    void Put(TrackInfo track);
}

/// <inheritdoc />
public sealed class YouTubeVideoCache(TimeProvider time) : IYouTubeVideoCache
{
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    internal const int Capacity = 500;

    private readonly ConcurrentDictionary<
        string,
        (TrackInfo Track, DateTimeOffset Expires)
    > _videos = new(StringComparer.Ordinal);

    public TrackInfo? Get(string videoId)
    {
        if (!_videos.TryGetValue(videoId, out (TrackInfo Track, DateTimeOffset Expires) entry))
            return null;
        if (entry.Expires > time.GetUtcNow())
            return entry.Track;

        _videos.TryRemove(videoId, out _);
        return null;
    }

    public void Put(TrackInfo track)
    {
        if (string.IsNullOrEmpty(track.ProviderTrackId))
            return;

        DateTimeOffset now = time.GetUtcNow();
        if (_videos.Count >= Capacity)
            Evict(now);
        _videos[track.ProviderTrackId] = (track, now + Lifetime);
    }

    /// <summary>Drops expired entries, then the ones closest to expiring until there is room again.</summary>
    private void Evict(DateTimeOffset now)
    {
        foreach (KeyValuePair<string, (TrackInfo Track, DateTimeOffset Expires)> entry in _videos)
        {
            if (entry.Value.Expires <= now)
                _videos.TryRemove(entry.Key, out _);
        }

        int excess = _videos.Count - Capacity + 1;
        if (excess <= 0)
            return;

        foreach (
            string videoId in _videos
                .OrderBy(entry => entry.Value.Expires)
                .Take(excess)
                .Select(entry => entry.Key)
        )
            _videos.TryRemove(videoId, out _);
    }
}

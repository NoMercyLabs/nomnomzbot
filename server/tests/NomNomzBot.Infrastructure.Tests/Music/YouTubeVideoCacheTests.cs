// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.Music;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>The resolved-video cache stays small and short-lived: entries expire, and it never grows past its cap.</summary>
public sealed class YouTubeVideoCacheTests
{
    [Fact]
    public void A_video_is_served_until_its_lifetime_ends()
    {
        FakeTimeProvider time = new(DateTimeOffset.Parse("2026-10-16T12:00:00Z"));
        YouTubeVideoCache cache = new(time);
        cache.Put(Track("dQw4w9WgXcQ"));

        time.Advance(YouTubeVideoCache.Lifetime - TimeSpan.FromSeconds(1));
        cache.Get("dQw4w9WgXcQ")!.TrackName.Should().Be("Song dQw4w9WgXcQ");

        time.Advance(TimeSpan.FromSeconds(1));
        cache.Get("dQw4w9WgXcQ").Should().BeNull();
    }

    [Fact]
    public void A_full_cache_drops_the_oldest_entry_to_make_room()
    {
        FakeTimeProvider time = new(DateTimeOffset.Parse("2026-10-16T12:00:00Z"));
        YouTubeVideoCache cache = new(time);
        for (int i = 0; i < YouTubeVideoCache.Capacity; i++)
        {
            cache.Put(Track($"video{i:D6}"));
            time.Advance(TimeSpan.FromMilliseconds(1));
        }

        cache.Put(Track("newestvideo"));

        cache.Get("video000000").Should().BeNull();
        cache.Get("video000001").Should().NotBeNull();
        cache.Get("newestvideo").Should().NotBeNull();
    }

    private static TrackInfo Track(string videoId) =>
        new()
        {
            TrackName = $"Song {videoId}",
            Artist = "Channel",
            Album = string.Empty,
            TrackUri = YouTubeMusicProvider.WatchUrl(videoId),
            Provider = "youtube",
            ProviderTrackId = videoId,
        };
}

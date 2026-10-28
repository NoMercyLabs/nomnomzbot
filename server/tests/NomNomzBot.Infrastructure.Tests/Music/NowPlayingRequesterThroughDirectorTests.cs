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

using FluentAssertions;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;
using static NomNomzBot.Infrastructure.Tests.Music.MusicServiceDirectorIntakeTests;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// S-NOWPLAYING-REQUESTER (Stoney, 2026-10-20): the requester's name was missing from Now playing on the
/// dashboard and on the overlay while History showed it. Only the request handed to the player while the
/// channel sat idle ever became "in flight", so every request that Spotify played from its own line-up
/// (the second song and all after it) was credited to nobody. The playback director knows which queue row
/// is playing, so it decides who asked.
/// </summary>
public sealed class NowPlayingRequesterThroughDirectorTests
{
    private const string FirstUri = "spotify:track:s1";
    private const string SecondUri = "spotify:track:s2";

    private static void SeedNowPlaying(NowPlayingCache cache, string trackId) =>
        cache.Set(
            SpotifyChannel,
            new()
            {
                TrackName = $"Song {trackId}",
                Artist = "Artist",
                Album = "Album",
                TrackUri = $"spotify:track:{trackId}",
                Provider = "spotify",
                IsPlaying = true,
                DurationMs = 200_000,
            },
            DateTimeOffset.UtcNow
        );

    /// <summary>Two viewers request one song each; the first plays, ends, and Spotify moves on to the second.</summary>
    private static async Task<(
        MusicService Sut,
        NowPlayingCache Cache,
        RecordingEventBus Bus,
        DirectorRig Rig
    )> SecondRequestPlayingAsync()
    {
        NowPlayingCache cache = new();
        RecordingEventBus bus = new();
        (MusicService sut, RecordingHttpHandler handler, DirectorRig rig) = BuildSpotify(
            cache,
            bus
        );
        FakeEngine spotify = rig.Engines.EngineOf(SpotifyChannel, EngineKind.Spotify);
        (await RequestAsync(sut, handler, "s1", "viewer-first")).IsSuccess.Should().BeTrue();
        (await RequestAsync(sut, handler, "s2", "viewer-second")).IsSuccess.Should().BeTrue();

        await spotify.EmitAsync(
            new EngineReport(EngineKind.Spotify, FirstUri, true, 1_000, 200_000)
        );
        await spotify.EmitAsync(new TrackEnded(EngineKind.Spotify, FirstUri));
        await spotify.EmitAsync(
            new EngineReport(EngineKind.Spotify, SecondUri, true, 1_000, 200_000)
        );

        return (sut, cache, bus, rig);
    }

    [Fact]
    public async Task The_viewer_whose_lined_up_request_Spotify_now_plays_is_named_as_the_requester()
    {
        (MusicService sut, NowPlayingCache cache, _, DirectorRig rig) =
            await SecondRequestPlayingAsync();
        rig.Registry.StateOf(SpotifyChannel)
            .Item!.RequestedBy.Should()
            .Be("viewer-second", "the director plays the second row");
        SeedNowPlaying(cache, "s2");

        NowPlaying? now = await sut.GetNowPlayingAsync(SpotifyChannel.ToString());

        now.Should().NotBeNull();
        now.TrackUri.Should().Be(SecondUri);
        now.RequestedBy.Should().Be("viewer-second");
    }

    [Fact]
    public async Task A_song_the_owner_plays_between_requests_is_credited_to_nobody()
    {
        (MusicService sut, NowPlayingCache cache, _, _) = await SecondRequestPlayingAsync();
        SeedNowPlaying(cache, "owner-song");

        NowPlaying? now = await sut.GetNowPlayingAsync(SpotifyChannel.ToString());

        now.Should().NotBeNull();
        now.RequestedBy.Should().BeNull();
    }
}

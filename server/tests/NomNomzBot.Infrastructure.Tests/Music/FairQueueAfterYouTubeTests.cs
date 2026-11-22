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
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music;
using static NomNomzBot.Infrastructure.Tests.Music.MusicServiceDirectorIntakeTests;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Stoney, 2026-11-22: a Spotify request lined up before a video was replaced by a request made during the
/// video when the video ended, and the lined-up song was queued again. A song that plays and is still a queue
/// row must not count twice for its owner, or the other owner's request passes it.
/// </summary>
public sealed class FairQueueAfterYouTubeTests
{
    private const string OwnUri = "spotify:track:own";
    private const string LinedUpUri = "spotify:track:a1";
    private const string OtherUri = "spotify:track:x";
    private const string VideoUri = "youtube:vid1";

    [Fact]
    public async Task A_request_lined_up_before_a_video_plays_in_full_after_it_and_a_later_request_waits()
    {
        NowPlayingCache cache = new();
        SongRequestQueueStore store = new();
        (MusicService sut, RecordingHttpHandler handler, DirectorRig rig) = BuildSpotify(
            cache,
            queueStore: store
        );
        FakeEngine spotify = rig.Engines.EngineOf(SpotifyChannel, EngineKind.Spotify);
        FakeEngine youTube = rig.Engines.EngineOf(SpotifyChannel, EngineKind.YouTube);
        await rig.Registry.SendAsync(SpotifyChannel, new OutputAttached());
        await spotify.EmitAsync(
            new EngineReport(EngineKind.Spotify, OwnUri, true, 190_000, 200_000)
        );

        store
            .GetOrCreate(SpotifyChannel.ToString())
            .Enqueue(
                "stoney_eagle",
                new(VideoUri, "Video", "Channel", null, 200_000, "stoney_eagle", 0, null, "YT01")
            );
        await rig.Registry.SendAsync(SpotifyChannel, new RequestAdded());
        (await RequestAsync(sut, handler, "a1", "MoehDev")).IsSuccess.Should().BeTrue();

        await spotify.EmitAsync(new TrackEnded(EngineKind.Spotify, OwnUri));
        await spotify.EmitAsync(
            new EngineReport(EngineKind.Spotify, OtherUri, true, 1_000, 200_000)
        );
        await spotify.EmitAsync(
            new EngineReport(EngineKind.Spotify, OtherUri, false, 1_000, 200_000)
        );
        await youTube.EmitAsync(
            new EngineReport(EngineKind.YouTube, VideoUri, true, 1_000, 200_000)
        );
        rig.Registry.StateOf(SpotifyChannel).Engine.Should().Be(EngineKind.YouTube);
        rig.Registry.StateOf(SpotifyChannel).Item!.Uri.Should().Be(VideoUri);

        (await RequestAsync(sut, handler, "b1", "stoney_eagle")).IsSuccess.Should().BeTrue();
        (await RequestAsync(sut, handler, "a2", "MoehDev")).IsSuccess.Should().BeTrue();

        await youTube.EmitAsync(new TrackEnding(EngineKind.YouTube, VideoUri));
        await youTube.EmitAsync(new TrackEnded(EngineKind.YouTube, VideoUri));
        await spotify.EmitAsync(new TrackEnded(EngineKind.Spotify, OtherUri));
        int callsBeforeA = spotify.Calls.Count;
        cache.Set(
            SpotifyChannel,
            new()
            {
                TrackName = "Song a1",
                Artist = "Artist",
                Album = "Album",
                TrackUri = LinedUpUri,
                Provider = "spotify",
                IsPlaying = true,
                DurationMs = 200_000,
            },
            DateTimeOffset.UtcNow
        );
        await sut.GetNowPlayingAsync(SpotifyChannel.ToString());
        await spotify.EmitAsync(
            new EngineReport(EngineKind.Spotify, LinedUpUri, true, 2_000, 200_000)
        );

        spotify
            .Calls.Skip(callsBeforeA)
            .Select(c => c.Name)
            .Should()
            .NotContain(["QueueNext", "Start", "Skip"]);
        PlaybackState after = rig.Registry.StateOf(SpotifyChannel);
        after.Kind.Should().Be(PlaybackStateKind.Playing);
        after.Item!.Uri.Should().Be(LinedUpUri);
        rig.Bus.Published.OfType<SongRequestPlayedEvent>()
            .Select(e => e.TrackUri)
            .Should()
            .Contain(LinedUpUri);
        store
            .GetOrCreate(SpotifyChannel.ToString())
            .GetSnapshot()
            .Select(e => e.Item.TrackUri)
            .Should()
            .Equal("spotify:track:b1", "spotify:track:a2");
    }
}

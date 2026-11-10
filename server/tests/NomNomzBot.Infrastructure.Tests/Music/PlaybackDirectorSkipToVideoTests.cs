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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music.Playback;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Live on 2026-11-10: a skip from a playing request to a video paused Spotify and recorded no hold, so
/// after the video the lined-up song waited on a paused Spotify for ever.
/// </summary>
public sealed class PlaybackDirectorSkipToVideoTests : IDisposable
{
    private static readonly PlaybackItem Request = Item("sp1");
    private static readonly PlaybackItem Video = Item("yt1", EngineKind.YouTube);
    private static readonly PlaybackItem NextRequest = Item("sp2");

    private readonly FakeEngine _spotify = new(EngineKind.Spotify);
    private readonly FakeEngine _youTube = new(EngineKind.YouTube);
    private readonly FairPlaybackQueue _queue = new();
    private readonly FakeTimeProvider _clock = NewClock();
    private readonly PlaybackDirector _director;

    public PlaybackDirectorSkipToVideoTests()
    {
        _director = new(Channel, [_spotify, _youTube], _queue, _clock, new RecordingBus());
    }

    public void Dispose() => _director.Dispose();

    private int Calls(string name) => _spotify.Calls.Count(call => call.Name == name);

    private Task SpotifyReportsAsync(PlaybackItem item, bool playing) =>
        _spotify.EmitAsync(
            new EngineReport(EngineKind.Spotify, item.Uri, playing, 90_000, 200_000)
        );

    private async Task RequestPlaysAsync()
    {
        _queue.Enqueue(Request);
        await _director.HandleAsync(new RequestAdded());
        await SpotifyReportsAsync(Request, true);
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, Request));
    }

    private async Task SkippedToVideoAsync()
    {
        await _director.HandleAsync(new OwnerSkip());
        await SpotifyReportsAsync(Request, false);
        await _youTube.EmitAsync(
            new EngineReport(EngineKind.YouTube, Video.Uri, true, 1_000, Video.DurationMs)
        );
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, Video));
        Calls("Resume").Should().Be(0);
    }

    private async Task LinedUpRowWaitsBehindVideoAsync()
    {
        await RequestPlaysAsync();
        _queue.Enqueue(Video);
        _queue.Enqueue(NextRequest);
        await _director.HandleAsync(new RequestAdded());
        _spotify.Held.Add(NextRequest.Id);
        await SkippedToVideoAsync();
    }

    [Fact]
    public async Task After_the_video_Spotify_skips_to_the_lined_up_song_it_was_paused_in_front_of()
    {
        await LinedUpRowWaitsBehindVideoAsync();
        int skipsBefore = Calls("Skip");

        await _youTube.EmitAsync(new TrackEnded(EngineKind.YouTube, Video.Uri));

        Calls("Skip").Should().Be(skipsBefore + 1);
        Calls("Resume").Should().Be(0);
        _queue.Snapshot.Select(row => row.Id).Should().Contain(NextRequest.Id);
    }

    [Fact]
    public async Task With_an_empty_queue_Spotify_moves_on_and_never_replays_the_skipped_song()
    {
        await RequestPlaysAsync();
        _queue.Enqueue(Video);
        await _director.HandleAsync(new RequestAdded());
        await SkippedToVideoAsync();
        int skipsBefore = Calls("Skip");

        await _youTube.EmitAsync(new TrackEnded(EngineKind.YouTube, Video.Uri));

        Calls("Skip").Should().Be(skipsBefore + 1);
        Calls("Resume").Should().Be(0);
        _director.State.Engine.Should().Be(EngineKind.Spotify);
    }

    [Fact]
    public async Task Previous_back_to_a_video_also_gives_Spotify_the_music_back_after_it()
    {
        _queue.Enqueue(Video);
        _queue.Enqueue(Request);
        await _director.HandleAsync(new RequestAdded());
        await _youTube.EmitAsync(
            new EngineReport(EngineKind.YouTube, Video.Uri, true, 1_000, Video.DurationMs)
        );
        await _youTube.EmitAsync(new TrackEnded(EngineKind.YouTube, Video.Uri));
        await _spotify.EmitAsync(
            new EngineReport(EngineKind.Spotify, Request.Uri, true, 1_000, 200_000)
        );
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, Request));
        _queue.Enqueue(NextRequest);
        await _director.HandleAsync(new RequestAdded());
        _spotify.Held.Add(NextRequest.Id);

        await _director.HandleAsync(new OwnerPrevious());
        await _spotify.EmitAsync(
            new EngineReport(EngineKind.Spotify, Request.Uri, false, 1_000, 200_000)
        );
        await _youTube.EmitAsync(
            new EngineReport(EngineKind.YouTube, Video.Uri, true, 1_000, Video.DurationMs)
        );
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, Video));
        int returnsBefore = Calls("Skip") + Calls("Resume");

        await _youTube.EmitAsync(new TrackEnded(EngineKind.YouTube, Video.Uri));

        (Calls("Skip") + Calls("Resume")).Should().Be(returnsBefore + 1);
    }
}

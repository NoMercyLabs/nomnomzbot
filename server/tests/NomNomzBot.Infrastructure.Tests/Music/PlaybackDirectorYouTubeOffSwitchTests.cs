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
/// The owner can turn YouTube off and Spotify is never held by it (Stoney, 2026-11-09: "i NEED it to not
/// break on me while i stream").
/// </summary>
public sealed class PlaybackDirectorYouTubeOffSwitchTests : IDisposable
{
    private const string OwnTrack = "spotify:track:made-up-own";
    private const string OwnNextTrack = "spotify:track:made-up-next";

    private static readonly PlaybackItem Video = Item("yt1", EngineKind.YouTube);
    private static readonly PlaybackItem OtherVideo = Item("yt2", EngineKind.YouTube, "bob");

    private readonly FakeEngine _spotify = new(EngineKind.Spotify);
    private readonly FakeEngine _youTube = new(EngineKind.YouTube);
    private readonly FairPlaybackQueue _queue = new();
    private readonly FakeTimeProvider _clock = NewClock();
    private readonly FakeSwitch _switch = new();
    private readonly PlaybackDirector _director;

    public PlaybackDirectorYouTubeOffSwitchTests()
    {
        _director = new(
            Channel,
            [_spotify, _youTube],
            _queue,
            _clock,
            new RecordingBus(),
            null,
            null,
            _switch
        );
    }

    public void Dispose() => _director.Dispose();

    private sealed class FakeSwitch : IYouTubeOffSwitch
    {
        public bool Off { get; set; }

        public int Reads { get; private set; }

        public Task<bool> IsOffAsync(Guid broadcasterId, CancellationToken ct = default)
        {
            Reads++;

            return Task.FromResult(Off);
        }
    }

    private int Calls(FakeEngine engine, string name) =>
        engine.Calls.Count(call => call.Name == name);

    private Task SpotifyReportsAsync(string uri, bool playing) =>
        _spotify.EmitAsync(new EngineReport(EngineKind.Spotify, uri, playing, 1_000, 200_000));

    private Task VideoReportsAsync(PlaybackItem video) =>
        _youTube.EmitAsync(
            new EngineReport(EngineKind.YouTube, video.Uri, true, 1_000, video.DurationMs)
        );

    /// <summary>Spotify plays, a video waits, Spotify's next song starts, so the director takes over.</summary>
    private async Task VideoIsTakingOverAsync()
    {
        await SpotifyReportsAsync(OwnTrack, true);
        _queue.Enqueue(Video);
        await _director.HandleAsync(new RequestAdded());
        await _spotify.EmitAsync(new TrackEnded(EngineKind.Spotify, OwnTrack));
        await SpotifyReportsAsync(OwnNextTrack, true);
    }

    private async Task VideoHoldsSpotifyAsync()
    {
        await VideoIsTakingOverAsync();
        await SpotifyReportsAsync(OwnNextTrack, false);
        await VideoReportsAsync(Video);
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, Video));
        Calls(_spotify, "Resume").Should().Be(0);
    }

    [Fact]
    public async Task Waiting_videos_leave_the_queue_within_20_seconds_and_Spotify_gets_no_command()
    {
        await SpotifyReportsAsync(OwnTrack, true);
        _queue.Enqueue(Video);
        _queue.Enqueue(OtherVideo);
        await _director.CheckDeadlinesAsync();
        int commandsBefore = _spotify.Calls.Count;

        _switch.Off = true;
        for (int second = 0; second < 19; second++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            await _director.CheckDeadlinesAsync();
        }

        _queue.Snapshot.Should().BeEmpty();
        _spotify.Calls.Count.Should().Be(commandsBefore);
        _youTube.Calls.Should().BeEmpty();
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
    }

    [Fact]
    public async Task The_setting_is_read_at_most_twice_in_20_seconds_while_a_video_waits()
    {
        await SpotifyReportsAsync(OwnTrack, true);
        _queue.Enqueue(Video);

        for (int second = 0; second < 20; second++)
        {
            await _director.CheckDeadlinesAsync();
            _clock.Advance(TimeSpan.FromSeconds(1));
        }

        _switch.Reads.Should().Be(2);
    }

    [Fact]
    public async Task With_no_video_around_the_setting_is_never_read_and_nothing_changes()
    {
        _switch.Off = true;
        await SpotifyReportsAsync(OwnTrack, true);
        int commandsBefore = _spotify.Calls.Count;

        await _director.CheckDeadlinesAsync();

        _switch.Reads.Should().Be(0);
        _spotify.Calls.Count.Should().Be(commandsBefore);
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
    }

    [Fact]
    public async Task A_playing_video_is_stopped_and_the_held_Spotify_gets_exactly_one_Resume()
    {
        await VideoHoldsSpotifyAsync();
        _queue.Enqueue(OtherVideo);
        int stopsBefore = Calls(_youTube, "Stop");

        _switch.Off = true;
        await _director.CheckDeadlinesAsync();

        Calls(_youTube, "Stop").Should().Be(stopsBefore + 1);
        Calls(_spotify, "Resume").Should().Be(1);
        _queue.Snapshot.Should().BeEmpty();
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        _youTube.Calls.Count(call => call is { Name: "Start", Argument: "yt2" }).Should().Be(0);
    }

    [Fact]
    public async Task A_video_still_waiting_for_Spotify_to_pause_never_starts_and_Spotify_comes_back()
    {
        await VideoIsTakingOverAsync();
        _director.State.Should().Be(PlaybackState.Starting(EngineKind.YouTube, Video));

        _switch.Off = true;
        await _director.CheckDeadlinesAsync();
        await SpotifyReportsAsync(OwnNextTrack, false);

        _queue.Snapshot.Should().BeEmpty();
        Calls(_youTube, "Start").Should().Be(0);
        Calls(_spotify, "Resume").Should().Be(1);
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
    }

    [Fact]
    public async Task A_video_the_owner_paused_while_Spotify_is_held_gives_the_music_back()
    {
        await VideoHoldsSpotifyAsync();
        await _director.HandleAsync(new OwnerPause());
        _director.State.Should().Be(PlaybackState.PausedByOwner(EngineKind.YouTube, Video));

        _switch.Off = true;
        await _director.CheckDeadlinesAsync();

        Calls(_spotify, "Resume").Should().Be(1);
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
    }

    [Fact]
    public async Task A_video_over_a_Spotify_that_was_not_playing_stops_and_Spotify_gets_no_Resume()
    {
        await SpotifyReportsAsync(OwnTrack, false);
        _queue.Enqueue(Video);
        await _director.HandleAsync(new RequestAdded());
        await VideoReportsAsync(Video);
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, Video));

        _switch.Off = true;
        await _director.CheckDeadlinesAsync();

        Calls(_youTube, "Stop").Should().Be(1);
        Calls(_spotify, "Resume").Should().Be(0);
        _director.State.Should().Be(PlaybackState.Idle);
    }

    [Fact]
    public async Task The_owners_own_Spotify_pause_stays_when_a_waiting_video_is_removed()
    {
        await SpotifyReportsAsync(OwnTrack, true);
        await _director.HandleAsync(new OwnerPause());
        _queue.Enqueue(Video);
        _director.State.Should().Be(PlaybackState.PausedByOwner(EngineKind.Spotify, null));

        _switch.Off = true;
        await _director.CheckDeadlinesAsync();

        _queue.Snapshot.Should().BeEmpty();
        Calls(_spotify, "Resume").Should().Be(0);
        _director.State.Should().Be(PlaybackState.PausedByOwner(EngineKind.Spotify, null));
    }

    [Fact]
    public async Task A_video_waiting_for_a_player_page_is_removed_and_the_block_ends()
    {
        _youTube.CanStart = false;
        _youTube.BlockCommands = EngineBlockReason.NoOutput;
        _queue.Enqueue(Video);
        await _director.HandleAsync(new RequestAdded());
        _director.State.Kind.Should().Be(PlaybackStateKind.Blocked);

        _switch.Off = true;
        await _director.CheckDeadlinesAsync();

        _queue.Snapshot.Should().BeEmpty();
        _director.State.Should().Be(PlaybackState.Idle);
        _director.BlockedReason.Should().BeNull();
    }

    [Fact]
    public async Task With_YouTube_on_the_video_and_the_hold_are_left_alone()
    {
        await VideoHoldsSpotifyAsync();
        _queue.Enqueue(OtherVideo);
        int stopsBefore = Calls(_youTube, "Stop");

        await _director.CheckDeadlinesAsync();

        Calls(_youTube, "Stop").Should().Be(stopsBefore);
        Calls(_spotify, "Resume").Should().Be(0);
        _queue.Snapshot.Select(row => row.Id).Should().Equal("yt2");
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, Video));
    }
}

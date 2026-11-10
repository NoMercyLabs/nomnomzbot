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
/// Any video failure gives Spotify the music back (Stoney, 2026-11-09: "i NEED it to not break on me while
/// i stream"), at every step of a run, and a Spotify the owner paused stays paused.
/// </summary>
public sealed class PlaybackDirectorYouTubeRunFailureTests : IDisposable
{
    private const string OwnTrack = "spotify:track:made-up-own";
    private const string OwnNextTrack = "spotify:track:made-up-next";

    private static readonly PlaybackItem FirstVideo = Item("yt1", EngineKind.YouTube);
    private static readonly PlaybackItem SecondVideo = Item("yt2", EngineKind.YouTube);

    private readonly FakeEngine _spotify = new(EngineKind.Spotify);
    private readonly FakeEngine _youTube = new(EngineKind.YouTube);
    private readonly FairPlaybackQueue _queue = new();
    private readonly FakeTimeProvider _clock = NewClock();
    private readonly FakeHeldStore _held = new();
    private readonly PlaybackDirector _director;

    public PlaybackDirectorYouTubeRunFailureTests()
    {
        _director = new(
            Channel,
            [_spotify, _youTube],
            _queue,
            _clock,
            new RecordingBus(),
            null,
            _held
        );
    }

    public void Dispose() => _director.Dispose();

    private sealed class FakeHeldStore : IHeldSpotifyStore
    {
        public DateTimeOffset? HeldSince { get; private set; }

        public Task SaveAsync(
            Guid broadcasterId,
            DateTimeOffset heldSince,
            CancellationToken ct = default
        )
        {
            HeldSince = heldSince;

            return Task.CompletedTask;
        }

        public Task<DateTimeOffset?> HeldSinceAsync(
            Guid broadcasterId,
            CancellationToken ct = default
        ) => Task.FromResult(HeldSince);

        public Task ClearAsync(Guid broadcasterId, CancellationToken ct = default)
        {
            HeldSince = null;

            return Task.CompletedTask;
        }
    }

    private int ResumesSent => _spotify.Calls.Count(call => call.Name == "Resume");

    private Task SpotifyReportsAsync(string uri, bool playing) =>
        _spotify.EmitAsync(new EngineReport(EngineKind.Spotify, uri, playing, 1_000, 200_000));

    private Task VideoReportsPlayingAsync(PlaybackItem video) =>
        _youTube.EmitAsync(
            new EngineReport(EngineKind.YouTube, video.Uri, true, 1_000, video.DurationMs)
        );

    private Task VideoEndsAsync(PlaybackItem video) =>
        _youTube.EmitAsync(new TrackEnded(EngineKind.YouTube, video.Uri));

    private async Task TakeoverPausedSpotifyAsync(params PlaybackItem[] videos)
    {
        await SpotifyReportsAsync(OwnTrack, true);
        foreach (PlaybackItem video in videos)
            _queue.Enqueue(video);

        await _director.HandleAsync(new RequestAdded());
        await _spotify.EmitAsync(new TrackEnded(EngineKind.Spotify, OwnTrack));
        await SpotifyReportsAsync(OwnNextTrack, false);
    }

    private async Task FirstVideoOfATwoVideoRunPlaysAsync()
    {
        await TakeoverPausedSpotifyAsync(FirstVideo, SecondVideo);
        await VideoReportsPlayingAsync(FirstVideo);
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, FirstVideo));
        _held.HeldSince.Should().NotBeNull();
    }

    [Fact]
    public async Task A_second_video_that_is_refused_gives_held_Spotify_the_music_back()
    {
        await FirstVideoOfATwoVideoRunPlaysAsync();
        _youTube.StartResult = EngineStartResult.Refused("made-up refusal");

        await VideoEndsAsync(FirstVideo);

        ResumesSent.Should().Be(1, "the director paused Spotify and this run has failed");
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        _queue.Head.Should().Be(SecondVideo, "one refusal does not drop the request");
        _held.HeldSince.Should().NotBeNull("nothing proved that Spotify plays yet");

        await SpotifyReportsAsync(OwnNextTrack, true);

        _held.HeldSince.Should().BeNull("Spotify reported playing");
        ResumesSent.Should().Be(1);
    }

    [Fact]
    public async Task A_second_video_that_times_out_gives_held_Spotify_the_music_back()
    {
        await FirstVideoOfATwoVideoRunPlaysAsync();
        await VideoEndsAsync(FirstVideo);
        _director.State.Should().Be(PlaybackState.Starting(EngineKind.YouTube, SecondVideo));

        _clock.Advance(TimeSpan.FromSeconds(11));
        await _director.CheckDeadlinesAsync();

        ResumesSent.Should().Be(1);
        _youTube.Calls.Should().Contain(call => call.Name == "Stop");
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
    }

    [Fact]
    public async Task A_paused_report_after_a_failed_second_video_is_not_read_as_the_owners_pause()
    {
        await FirstVideoOfATwoVideoRunPlaysAsync();
        _youTube.StartResult = EngineStartResult.Refused("made-up refusal");
        await VideoEndsAsync(FirstVideo);
        _clock.Advance(TimeSpan.FromSeconds(6));

        await SpotifyReportsAsync(OwnNextTrack, false);

        _director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        ResumesSent.Should().Be(1, "an accepted Resume is never sent again");
    }

    [Fact]
    public async Task A_second_video_with_no_page_left_shows_the_block_and_gives_Spotify_back()
    {
        await FirstVideoOfATwoVideoRunPlaysAsync();
        _youTube.BlockCommands = EngineBlockReason.NoOutput;

        await VideoEndsAsync(FirstVideo);

        ResumesSent.Should().Be(1);
        _director.State.Kind.Should().Be(PlaybackStateKind.Blocked);
        _queue.Head.Should().Be(SecondVideo, "the request waits for a page");

        await SpotifyReportsAsync(OwnNextTrack, true);

        _director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        _held.HeldSince.Should().BeNull();
    }

    [Fact]
    public async Task A_run_the_owners_skip_started_also_gives_Spotify_back_when_its_second_video_fails()
    {
        await SpotifyReportsAsync(OwnTrack, true);
        _queue.Enqueue(FirstVideo);
        _queue.Enqueue(SecondVideo);
        await _director.HandleAsync(new OwnerSkip());
        await SpotifyReportsAsync(OwnTrack, false);
        await VideoReportsPlayingAsync(FirstVideo);
        _youTube.StartResult = EngineStartResult.Refused("made-up refusal");

        await VideoEndsAsync(FirstVideo);

        ResumesSent.Should().Be(1);
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
    }

    [Fact]
    public async Task A_failed_second_video_never_resumes_a_Spotify_the_owner_paused()
    {
        await SpotifyReportsAsync(OwnTrack, true);
        await _director.HandleAsync(new OwnerPause());
        _queue.Enqueue(FirstVideo);
        _queue.Enqueue(SecondVideo);
        await _director.HandleAsync(new OwnerSkip());
        await VideoReportsPlayingAsync(FirstVideo);
        _youTube.StartResult = EngineStartResult.Refused("made-up refusal");

        await VideoEndsAsync(FirstVideo);

        ResumesSent.Should().Be(0, "the owner paused Spotify, the director did not");
    }

    [Fact]
    public async Task Skip_on_a_paused_video_with_an_empty_queue_gives_held_Spotify_the_music_back()
    {
        await TakeoverPausedSpotifyAsync(FirstVideo);
        await VideoReportsPlayingAsync(FirstVideo);
        await _director.HandleAsync(new OwnerPause());
        _director.State.Should().Be(PlaybackState.PausedByOwner(EngineKind.YouTube, FirstVideo));

        await _director.HandleAsync(new OwnerSkip());

        ResumesSent.Should().Be(1);
        _youTube.Calls.Should().Contain(call => call.Name == "Stop");
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));

        await SpotifyReportsAsync(OwnNextTrack, true);

        _held.HeldSince.Should().BeNull();
        ResumesSent.Should().Be(1);
    }

    [Fact]
    public async Task Skip_on_a_paused_video_the_owners_skip_started_gives_Spotify_the_music_back()
    {
        await SpotifyReportsAsync(OwnTrack, true);
        _queue.Enqueue(FirstVideo);
        await _director.HandleAsync(new OwnerSkip());
        await SpotifyReportsAsync(OwnTrack, false);
        await VideoReportsPlayingAsync(FirstVideo);
        await _director.HandleAsync(new OwnerPause());

        await _director.HandleAsync(new OwnerSkip());

        ResumesSent.Should().Be(1);
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
    }

    [Fact]
    public async Task Skip_on_a_paused_video_never_resumes_a_Spotify_the_owner_paused()
    {
        await SpotifyReportsAsync(OwnTrack, true);
        await _director.HandleAsync(new OwnerPause());
        _queue.Enqueue(FirstVideo);
        await _director.HandleAsync(new OwnerSkip());
        await VideoReportsPlayingAsync(FirstVideo);
        await _director.HandleAsync(new OwnerPause());

        await _director.HandleAsync(new OwnerSkip());

        ResumesSent.Should().Be(0);
        _director.State.Should().Be(PlaybackState.Idle);
    }

    [Fact]
    public async Task A_refused_first_video_clears_the_hold_so_a_later_video_end_leaves_Spotify_paused()
    {
        _youTube.StartResult = EngineStartResult.Refused("made-up refusal");
        await TakeoverPausedSpotifyAsync(FirstVideo);
        ResumesSent.Should().Be(1, "the takeover paused Spotify and the video was refused");
        _held.HeldSince.Should().NotBeNull("nothing proved that Spotify plays yet");
        await SpotifyReportsAsync(OwnNextTrack, true);
        _held.HeldSince.Should().BeNull();

        await _director.HandleAsync(new OwnerPause());
        _youTube.StartResult = EngineStartResult.Ok;
        await _director.HandleAsync(new OwnerSkip());
        await VideoReportsPlayingAsync(FirstVideo);
        await VideoEndsAsync(FirstVideo);

        ResumesSent.Should().Be(1, "the owner paused Spotify after the failed start");
        _spotify.Calls.Should().NotContain(call => call.Name == "Skip");
        _director.State.Should().Be(PlaybackState.Idle);
    }
}

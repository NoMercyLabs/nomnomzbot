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
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music.Playback;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// A Resume that fails must not leave Spotify paused for good (Stoney, 2026-11-09: "i NEED it to not break
/// on me while i stream"). The hold ends when Spotify reports playing; until then the director retries.
/// </summary>
public sealed class PlaybackDirectorSpotifyReturnRetryTests : IDisposable
{
    private const string OwnTrack = "spotify:track:made-up-own";
    private const string OwnNextTrack = "spotify:track:made-up-next";

    private static readonly PlaybackItem Video = Item("yt1", EngineKind.YouTube);

    private readonly FakeEngine _innerSpotify = new(EngineKind.Spotify);
    private readonly FakeEngine _youTube = new(EngineKind.YouTube);
    private readonly FairPlaybackQueue _queue = new();
    private readonly FakeTimeProvider _clock = NewClock();
    private readonly FakeHeldStore _held = new();
    private readonly FailingSpotify _spotify;
    private readonly PlaybackDirector _director;

    public PlaybackDirectorSpotifyReturnRetryTests()
    {
        _spotify = new(_innerSpotify);
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

    /// <summary>Fails the commands a test names with a plain exception, as a 5xx from Spotify would.</summary>
    private sealed class FailingSpotify(FakeEngine inner) : IPlaybackEngine
    {
        public int ResumeFailures { get; set; }

        public int SkipFailures { get; set; }

        public int ResumesSent { get; private set; }

        public int SkipsSent { get; private set; }

        public EngineKind Kind => inner.Kind;

        public bool LinesUpInItsOwnQueue => inner.LinesUpInItsOwnQueue;

        public event Func<EngineSignal, Task> Reports
        {
            add => inner.Reports += value;
            remove => inner.Reports -= value;
        }

        public Task ResumeAsync(CancellationToken ct = default)
        {
            ResumesSent++;
            if (ResumeFailures <= 0)
                return inner.ResumeAsync(ct);

            ResumeFailures--;
            throw new HttpRequestException("made-up 503");
        }

        public Task SkipAsync(CancellationToken ct = default)
        {
            SkipsSent++;
            if (SkipFailures <= 0)
                return inner.SkipAsync(ct);

            SkipFailures--;
            throw new HttpRequestException("made-up 503");
        }

        public Task<EngineStartResult> StartAsync(
            PlaybackItem item,
            CancellationToken ct = default
        ) => inner.StartAsync(item, ct);

        public Task<EngineStartResult> QueueNextAsync(
            PlaybackItem item,
            CancellationToken ct = default
        ) => inner.QueueNextAsync(item, ct);

        public Task<bool> IsStillQueuedAsync(PlaybackItem item, CancellationToken ct = default) =>
            inner.IsStillQueuedAsync(item, ct);

        public Task PauseAsync(CancellationToken ct = default) => inner.PauseAsync(ct);

        public Task StopAsync(CancellationToken ct = default) => inner.StopAsync(ct);

        public Task<bool> CanStartAsync(CancellationToken ct = default) => inner.CanStartAsync(ct);

        public Task SeekAsync(int positionMs, CancellationToken ct = default) =>
            inner.SeekAsync(positionMs, ct);

        public Task SetVolumeAsync(int level, CancellationToken ct = default) =>
            inner.SetVolumeAsync(level, ct);

        public Task<EngineReport?> ProbeAsync(CancellationToken ct = default) =>
            inner.ProbeAsync(ct);
    }

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

    private static EngineReport SpotifyReport(string uri, bool playing) =>
        new(EngineKind.Spotify, uri, playing, 1_000, 200_000);

    private Task SpotifyReportsAsync(string uri, bool playing) =>
        _innerSpotify.EmitAsync(SpotifyReport(uri, playing));

    private Task TickAsync() => _director.HandleAsync(new RequestAdded());

    /// <summary>Spotify moved on to its next song, the takeover paused it, and the video was asked to start.</summary>
    private async Task SpotifyPausedForTheVideoAsync(PlaybackItem? linedUp = null)
    {
        await SpotifyReportsAsync(OwnTrack, true);
        _queue.Enqueue(Video);
        await TickAsync();
        if (linedUp is not null)
        {
            _queue.Enqueue(linedUp);
            await TickAsync();
        }

        await _innerSpotify.EmitAsync(new TrackEnded(EngineKind.Spotify, OwnTrack));
        await SpotifyReportsAsync(OwnNextTrack, true);
        await SpotifyReportsAsync(OwnNextTrack, false);
    }

    private async Task VideoHoldsSpotifyAsync(PlaybackItem? linedUp = null)
    {
        await SpotifyPausedForTheVideoAsync(linedUp);
        await _youTube.EmitAsync(
            new EngineReport(EngineKind.YouTube, Video.Uri, true, 1_000, Video.DurationMs)
        );
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, Video));
        _held.HeldSince.Should().NotBeNull();
    }

    private Task VideoEndsAsync() =>
        _youTube.EmitAsync(new TrackEnded(EngineKind.YouTube, Video.Uri));

    private async Task TimerRunsForAsync(int seconds)
    {
        for (int second = 0; second < seconds; second++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            await _director.CheckDeadlinesAsync();
        }
    }

    [Fact]
    public async Task A_Resume_that_throws_once_is_sent_again_and_the_hold_clears_when_Spotify_plays()
    {
        await VideoHoldsSpotifyAsync();
        _spotify.ResumeFailures = 1;

        await VideoEndsAsync();

        _spotify.ResumesSent.Should().Be(1);
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        _held.HeldSince.Should().NotBeNull("the failed Resume gave nothing back yet");

        await TimerRunsForAsync(2);

        _spotify.ResumesSent.Should().Be(2);
        _held.HeldSince.Should().NotBeNull("Spotify has not reported playing");

        await SpotifyReportsAsync(OwnNextTrack, true);
        await TimerRunsForAsync(60);

        _spotify.ResumesSent.Should().Be(2, "a playing Spotify needs no further Resume");
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        _held.HeldSince.Should().BeNull();
    }

    [Fact]
    public async Task A_Resume_that_keeps_failing_ends_in_a_visible_Blocked_after_a_bounded_number_of_sends()
    {
        await VideoHoldsSpotifyAsync();
        _spotify.ResumeFailures = int.MaxValue;

        await VideoEndsAsync();
        await TimerRunsForAsync(120);

        _spotify.ResumesSent.Should().Be(4);
        _director.State.Should().Be(PlaybackState.Blocked("SpotifyResumeFailed"));
        _held.HeldSince.Should().NotBeNull("a restart may still give the music back");

        await TickAsync();

        _director
            .State.Should()
            .Be(
                PlaybackState.Blocked("SpotifyResumeFailed"),
                "a poll tick must not hide the error"
            );
    }

    [Fact]
    public async Task A_typed_refusal_keeps_its_reason_when_the_retries_run_out()
    {
        await VideoHoldsSpotifyAsync();
        _innerSpotify.BlockCommands = EngineBlockReason.NoOutput;

        await VideoEndsAsync();
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        await TimerRunsForAsync(120);

        _spotify.ResumesSent.Should().Be(4);
        _director.State.Should().Be(PlaybackState.Blocked(nameof(EngineBlockReason.NoOutput)));
    }

    [Fact]
    public async Task The_held_fact_stays_through_paused_reports_until_Spotify_reports_playing()
    {
        await VideoHoldsSpotifyAsync();

        await VideoEndsAsync();
        _held.HeldSince.Should().NotBeNull();

        await TimerRunsForAsync(1);
        await SpotifyReportsAsync(OwnNextTrack, false);
        _clock.Advance(TimeSpan.FromSeconds(6));
        await SpotifyReportsAsync(OwnNextTrack, false);

        _held.HeldSince.Should().NotBeNull();
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));

        await SpotifyReportsAsync(OwnNextTrack, true);

        _held.HeldSince.Should().BeNull();
    }

    [Fact]
    public async Task An_accepted_Resume_that_never_makes_Spotify_play_still_ends_in_Blocked()
    {
        await VideoHoldsSpotifyAsync();

        await VideoEndsAsync();
        await TimerRunsForAsync(120);

        _spotify.ResumesSent.Should().Be(1);
        _director.State.Kind.Should().Be(PlaybackStateKind.Blocked);
    }

    [Fact]
    public async Task A_probe_that_finds_Spotify_playing_ends_the_retries()
    {
        await VideoHoldsSpotifyAsync();
        await VideoEndsAsync();
        _innerSpotify.ProbeReport = SpotifyReport(OwnNextTrack, true);

        await TimerRunsForAsync(60);

        _spotify.ResumesSent.Should().Be(1);
        _held.HeldSince.Should().BeNull();
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
    }

    [Fact]
    public async Task The_owners_pause_during_the_retries_is_never_undone()
    {
        await VideoHoldsSpotifyAsync();
        _spotify.ResumeFailures = 1;
        await VideoEndsAsync();

        await _director.HandleAsync(new OwnerPause());
        await TimerRunsForAsync(60);

        _spotify.ResumesSent.Should().Be(1);
        _held.HeldSince.Should().BeNull("a restart must not resume what the owner paused");
    }

    [Fact]
    public async Task A_Skip_that_was_accepted_is_never_sent_twice()
    {
        PlaybackItem song = Item("sp1");
        await VideoHoldsSpotifyAsync(song);

        await VideoEndsAsync();
        await TimerRunsForAsync(4);

        _spotify.SkipsSent.Should().Be(1, "a second Skip would lose a song");
        _spotify.ResumesSent.Should().Be(0, "an accepted send is never followed by another");
    }

    [Fact]
    public async Task A_Skip_that_threw_is_sent_again()
    {
        PlaybackItem song = Item("sp1");
        await VideoHoldsSpotifyAsync(song);
        _spotify.SkipFailures = 1;

        await VideoEndsAsync();
        _spotify.SkipsSent.Should().Be(1);
        await TimerRunsForAsync(2);

        _spotify.SkipsSent.Should().Be(2);
        _spotify.ResumesSent.Should().Be(0);
    }

    [Fact]
    public async Task A_failed_video_start_retries_the_Resume_of_the_music_it_paused()
    {
        _youTube.StartResult = EngineStartResult.Refused("made-up refusal");
        _spotify.ResumeFailures = 1;

        await SpotifyPausedForTheVideoAsync();

        _spotify.ResumesSent.Should().Be(1);
        _held.HeldSince.Should().NotBeNull();

        await TimerRunsForAsync(2);

        _spotify.ResumesSent.Should().Be(2);

        await SpotifyReportsAsync(OwnNextTrack, true);

        _held.HeldSince.Should().BeNull();
    }
}

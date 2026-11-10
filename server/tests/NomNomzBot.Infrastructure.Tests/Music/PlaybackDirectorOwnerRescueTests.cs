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
/// The owner can always get Spotify back, and his own pause stays (Stoney, 2026-11-09: "i NEED it to not
/// break on me while i stream").
/// </summary>
public sealed class PlaybackDirectorOwnerRescueTests : IDisposable
{
    private const string OwnTrack = "spotify:track:made-up-own";
    private const string OwnNextTrack = "spotify:track:made-up-next";

    private static readonly TimeSpan ReportSilence = TimeSpan.FromMilliseconds(15_001);
    private static readonly PlaybackItem Video = Item("yt1", EngineKind.YouTube);

    private readonly FakeEngine _spotify = new(EngineKind.Spotify);
    private readonly FakeEngine _youTube = new(EngineKind.YouTube);
    private readonly FairPlaybackQueue _queue = new();
    private readonly FakeTimeProvider _clock = NewClock();
    private readonly FakeHeldStore _held = new();
    private readonly PlaybackDirector _director;

    public PlaybackDirectorOwnerRescueTests()
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

    private int SpotifyCalls(string name) => _spotify.Calls.Count(call => call.Name == name);

    private Task SpotifyReportsAsync(string uri, bool playing) =>
        _spotify.EmitAsync(new EngineReport(EngineKind.Spotify, uri, playing, 1_000, 200_000));

    private Task VideoReportsAsync(PlaybackItem video) =>
        _youTube.EmitAsync(
            new EngineReport(EngineKind.YouTube, video.Uri, true, 1_000, video.DurationMs)
        );

    private Task TickAsync() => _director.HandleAsync(new RequestAdded());

    private async Task VideoHoldsSpotifyAsync(PlaybackItem? linedUp = null)
    {
        await SpotifyReportsAsync(OwnTrack, true);
        _queue.Enqueue(Video);
        await TickAsync();
        if (linedUp is not null)
        {
            _queue.Enqueue(linedUp);
            await TickAsync();
        }

        await _spotify.EmitAsync(new TrackEnded(EngineKind.Spotify, OwnTrack));
        await SpotifyReportsAsync(OwnNextTrack, true);
        await SpotifyReportsAsync(OwnNextTrack, false);
        await VideoReportsAsync(Video);
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
    public async Task A_dashboard_resume_during_a_held_video_stops_it_and_gives_Spotify_the_music()
    {
        await VideoHoldsSpotifyAsync();

        PlaybackInputOutcome outcome = await _director.HandleAsync(new OwnerResume());

        outcome.Should().Be(PlaybackInputOutcome.Applied);
        SpotifyCalls("Resume").Should().Be(1);
        _youTube.Calls.Should().Contain(new EngineCall("Stop"));
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));

        await SpotifyReportsAsync(OwnNextTrack, true);

        _held.HeldSince.Should().BeNull();
    }

    [Fact]
    public async Task A_dashboard_resume_with_the_player_page_gone_still_gives_Spotify_the_music()
    {
        await VideoHoldsSpotifyAsync();
        _youTube.BlockCommands = EngineBlockReason.NoOutput;

        await _director.HandleAsync(new OwnerResume());

        SpotifyCalls("Resume").Should().Be(1);
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
    }

    [Fact]
    public async Task A_dashboard_resume_of_a_paused_video_whose_page_is_gone_gives_Spotify_the_music()
    {
        await VideoHoldsSpotifyAsync();
        await _director.HandleAsync(new OwnerPause());
        _youTube.BlockCommands = EngineBlockReason.NoOutput;

        await _director.HandleAsync(new OwnerResume());

        SpotifyCalls("Resume").Should().Be(1);
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
    }

    [Fact]
    public async Task A_dashboard_resume_during_the_retries_sends_the_Resume_now()
    {
        await VideoHoldsSpotifyAsync();
        await VideoEndsAsync();
        SpotifyCalls("Resume").Should().Be(1);

        PlaybackInputOutcome outcome = await _director.HandleAsync(new OwnerResume());

        outcome.Should().Be(PlaybackInputOutcome.Applied);
        SpotifyCalls("Resume").Should().Be(2);
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
    }

    [Fact]
    public async Task A_dashboard_resume_after_the_retries_gave_up_clears_the_Blocked_and_tries_again()
    {
        await VideoHoldsSpotifyAsync();
        _spotify.BlockCommands = EngineBlockReason.NoOutput;
        await VideoEndsAsync();
        await TimerRunsForAsync(120);
        _director.State.Should().Be(PlaybackState.Blocked(nameof(EngineBlockReason.NoOutput)));
        int sent = SpotifyCalls("Resume");
        _spotify.BlockCommands = null;

        PlaybackInputOutcome outcome = await _director.HandleAsync(new OwnerResume());

        outcome.Should().Be(PlaybackInputOutcome.Applied);
        SpotifyCalls("Resume").Should().Be(sent + 1);
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));

        await SpotifyReportsAsync(OwnNextTrack, true);

        _held.HeldSince.Should().BeNull();
    }

    [Fact]
    public async Task A_dashboard_resume_during_a_video_never_undoes_the_owners_own_Spotify_pause()
    {
        await SpotifyReportsAsync(OwnTrack, false);
        _queue.Enqueue(Video);
        await TickAsync();
        await VideoReportsAsync(Video);

        await _director.HandleAsync(new OwnerResume());

        SpotifyCalls("Resume").Should().Be(0);
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, Video));
    }

    [Fact]
    public async Task A_Spotify_play_past_the_report_deadline_is_followed_with_no_command_to_Spotify()
    {
        await VideoHoldsSpotifyAsync();
        int pauses = SpotifyCalls("Pause");
        _clock.Advance(ReportSilence);

        await SpotifyReportsAsync(OwnNextTrack, true);
        await TimerRunsForAsync(60);

        SpotifyCalls("Resume").Should().Be(0, "the owner already made Spotify play");
        SpotifyCalls("Pause").Should().Be(pauses);
        _youTube.Calls.Should().Contain(new EngineCall("Stop"));
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        _held.HeldSince.Should().BeNull();
    }

    [Fact]
    public async Task A_Spotify_play_past_the_report_deadline_is_not_skipped_to_the_lined_up_song()
    {
        await VideoHoldsSpotifyAsync(Item("sp1"));
        _clock.Advance(ReportSilence);

        await SpotifyReportsAsync(OwnNextTrack, true);

        SpotifyCalls("Skip").Should().Be(0, "a Skip would throw away the song the owner started");
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
    }

    [Fact]
    public async Task A_Spotify_play_past_the_report_deadline_is_not_paused_for_the_next_video()
    {
        PlaybackItem nextVideo = Item("yt2", EngineKind.YouTube);
        _queue.Enqueue(Video);
        await TickAsync();
        await VideoReportsAsync(Video);
        _queue.Enqueue(nextVideo);
        _clock.Advance(ReportSilence);

        await SpotifyReportsAsync(OwnTrack, true);

        SpotifyCalls("Pause").Should().Be(0);
        _youTube.Calls.Should().NotContain(new EngineCall("Start", nextVideo.Id));
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
    }

    [Fact]
    public async Task A_Spotify_play_before_the_report_deadline_is_still_silenced()
    {
        await VideoHoldsSpotifyAsync();
        int pauses = SpotifyCalls("Pause");
        _clock.Advance(TimeSpan.FromSeconds(6));

        await SpotifyReportsAsync(OwnNextTrack, true);

        SpotifyCalls("Pause").Should().Be(pauses + 1);
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, Video));
    }

    [Fact]
    public async Task An_accepted_Resume_is_never_sent_again_and_an_unconfirmed_one_ends_in_Blocked()
    {
        await VideoHoldsSpotifyAsync();
        await VideoEndsAsync();
        SpotifyCalls("Resume").Should().Be(1);

        await TimerRunsForAsync(1);
        await SpotifyReportsAsync(OwnNextTrack, false);
        await TimerRunsForAsync(120);

        SpotifyCalls("Resume").Should().Be(1, "a second Resume could undo the owner's pause");
        _director.State.Should().Be(PlaybackState.Blocked("SpotifyResumeFailed"));
    }

    [Fact]
    public async Task A_pause_after_Spotify_reported_playing_gets_no_Resume_and_counts_as_the_owners()
    {
        await VideoHoldsSpotifyAsync();
        await VideoEndsAsync();
        SpotifyCalls("Resume").Should().Be(1);

        await SpotifyReportsAsync(OwnNextTrack, true);
        _clock.Advance(TimeSpan.FromSeconds(1));
        await SpotifyReportsAsync(OwnNextTrack, false);
        await TimerRunsForAsync(60);

        SpotifyCalls("Resume").Should().Be(1, "the owner's pause is never undone");
        _director.State.Should().Be(PlaybackState.PausedByOwner(EngineKind.Spotify, null));
        _held.HeldSince.Should().BeNull();

        await _director.HandleAsync(new OwnerResume());

        SpotifyCalls("Resume").Should().Be(2, "his own resume still reaches Spotify");
        _director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
    }
}

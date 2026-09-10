// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Music.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// S-PL9/S-PL11 — the generic "play one track now" pipeline primitive (go-live intro, raid-start song,
/// any other automation): <see cref="MusicPlayTrackOnceAction"/> captures the ambient playback context
/// and interrupts it, <see cref="PlayOnceResumeHandler"/> puts it back once the interrupting track is
/// over. Proves the full chain — capture, interrupt, and the state-change consequence of the
/// track-finished signal — not just "no exception".
/// </summary>
public sealed class PlayTrackOnceTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000ac003");
    private const string InterruptingTrackUri = "spotify:track:intro-song";
    private const string PriorTrackUri = "spotify:track:auto-playlist-track";

    private static PipelineExecutionContext Ctx() =>
        new()
        {
            BroadcasterId = ChannelId,
            TriggeredByUserId = "twitch-42",
            TriggeredByDisplayName = "Bamo",
            MessageId = "msg-1",
            RawMessage = "raid start",
        };

    private static ActionDefinition Def(params (string Key, string Value)[] parameters) =>
        new()
        {
            Type = "play_track_once",
            Parameters = parameters.ToDictionary(
                p => p.Key,
                p => JsonSerializer.SerializeToElement(p.Value)
            ),
        };

    private static NowPlaying Playing(string trackUri, int progressMs, bool isPlaying) =>
        new(
            "Auto Playlist Track",
            "Artist",
            "Album",
            null,
            200_000,
            progressMs,
            isPlaying,
            100,
            null,
            "spotify",
            trackUri
        );

    // ─── MusicPlayTrackOnceAction: capture + interrupt ─────────────────────────

    [Fact]
    public async Task Executing_captures_the_prior_context_and_plays_the_configured_track_now()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Playing(PriorTrackUri, progressMs: 42_000, isPlaying: true));
        music
            .PlayTrackOnceAsync(
                ChannelId.ToString(),
                InterruptingTrackUri,
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        PlayOnceResumeTracker tracker = new();
        MusicPlayTrackOnceAction action = new(music, tracker);

        ActionResult result = await action.ExecuteAsync(
            Ctx(),
            Def(("track_uri", InterruptingTrackUri))
        );

        result.Succeeded.Should().BeTrue();
        await music
            .Received(1)
            .PlayTrackOnceAsync(
                ChannelId.ToString(),
                InterruptingTrackUri,
                Arg.Any<CancellationToken>()
            );

        // The consequence, not just the call: the tracker now really holds the captured context, ready
        // for PlayOnceResumeHandler to put it back later.
        tracker.TryPeek(ChannelId, out PlayOnceResumeState pending).Should().BeTrue();
        pending.InterruptingTrackUri.Should().Be(InterruptingTrackUri);
        pending.PriorTrackUri.Should().Be(PriorTrackUri);
        pending.PriorProgressMs.Should().Be(42_000);
        pending.PriorWasPlaying.Should().BeTrue();
    }

    [Fact]
    public async Task Executing_with_nothing_playing_beforehand_remembers_no_prior_track()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns((NowPlaying?)null);
        music
            .PlayTrackOnceAsync(
                ChannelId.ToString(),
                InterruptingTrackUri,
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        PlayOnceResumeTracker tracker = new();
        MusicPlayTrackOnceAction action = new(music, tracker);

        await action.ExecuteAsync(Ctx(), Def(("track_uri", InterruptingTrackUri)));

        tracker.TryPeek(ChannelId, out PlayOnceResumeState pending).Should().BeTrue();
        pending.PriorTrackUri.Should().BeNull();
    }

    [Fact]
    public async Task A_failed_play_clears_the_captured_context_instead_of_arming_a_phantom_resume()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Playing(PriorTrackUri, progressMs: 1_000, isPlaying: true));
        music
            .PlayTrackOnceAsync(
                ChannelId.ToString(),
                InterruptingTrackUri,
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure("no active device", "NO_ACTIVE_DEVICE"));
        PlayOnceResumeTracker tracker = new();
        MusicPlayTrackOnceAction action = new(music, tracker);

        ActionResult result = await action.ExecuteAsync(
            Ctx(),
            Def(("track_uri", InterruptingTrackUri))
        );

        result.Succeeded.Should().BeFalse();
        tracker.TryPeek(ChannelId, out _).Should().BeFalse();
    }

    // ─── PlayOnceResumeHandler: the track-finished signal resumes the prior context ───

    [Fact]
    public async Task The_interrupting_tracks_own_start_event_does_not_trigger_a_resume()
    {
        PlayOnceResumeTracker tracker = new();
        tracker.Remember(
            ChannelId,
            new(InterruptingTrackUri, PriorTrackUri, PriorProgressMs: 42_000, PriorWasPlaying: true)
        );
        IMusicService music = Substitute.For<IMusicService>();
        PlayOnceResumeHandler handler = new(
            tracker,
            music,
            NullLogger<PlayOnceResumeHandler>.Instance
        );

        await handler.HandleAsync(
            new PlaybackStateChangedEvent
            {
                BroadcasterId = ChannelId,
                IsPlaying = true,
                TrackUri = InterruptingTrackUri,
            }
        );

        await music
            .DidNotReceive()
            .PlayTrackOnceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        // Still armed — the interruption isn't over yet.
        tracker.TryPeek(ChannelId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Once_the_interrupting_track_moves_on_the_prior_track_resumes_at_its_captured_position()
    {
        PlayOnceResumeTracker tracker = new();
        tracker.Remember(
            ChannelId,
            new(InterruptingTrackUri, PriorTrackUri, PriorProgressMs: 42_000, PriorWasPlaying: true)
        );
        IMusicService music = Substitute.For<IMusicService>();
        music
            .PlayTrackOnceAsync(ChannelId.ToString(), PriorTrackUri, Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        music
            .SeekAsync(ChannelId.ToString(), 42_000, Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        PlayOnceResumeHandler handler = new(
            tracker,
            music,
            NullLogger<PlayOnceResumeHandler>.Instance
        );

        // The provider's own state now reports a DIFFERENT track — the interrupting track has finished
        // (or was otherwise moved past), which is the real "track finished" signal this seam already
        // publishes on every playback-state change.
        await handler.HandleAsync(
            new PlaybackStateChangedEvent
            {
                BroadcasterId = ChannelId,
                IsPlaying = true,
                TrackUri = "spotify:track:whatever-the-provider-moved-to-next",
            }
        );

        await music
            .Received(1)
            .PlayTrackOnceAsync(ChannelId.ToString(), PriorTrackUri, Arg.Any<CancellationToken>());
        await music
            .Received(1)
            .SeekAsync(ChannelId.ToString(), 42_000, Arg.Any<CancellationToken>());
        await music.DidNotReceive().PauseAsync(ChannelId.ToString(), Arg.Any<CancellationToken>());
        // The resume fired exactly once — nothing left armed for a second event to double-resume.
        tracker.TryPeek(ChannelId, out _).Should().BeFalse();
    }

    [Fact]
    public async Task Resuming_a_track_that_was_paused_before_the_interruption_pauses_it_back()
    {
        PlayOnceResumeTracker tracker = new();
        tracker.Remember(
            ChannelId,
            new(InterruptingTrackUri, PriorTrackUri, PriorProgressMs: 5_000, PriorWasPlaying: false)
        );
        IMusicService music = Substitute.For<IMusicService>();
        music
            .PlayTrackOnceAsync(ChannelId.ToString(), PriorTrackUri, Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        music
            .SeekAsync(ChannelId.ToString(), 5_000, Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        music
            .PauseAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        PlayOnceResumeHandler handler = new(
            tracker,
            music,
            NullLogger<PlayOnceResumeHandler>.Instance
        );

        await handler.HandleAsync(
            new PlaybackStateChangedEvent
            {
                BroadcasterId = ChannelId,
                IsPlaying = true,
                TrackUri = "spotify:track:something-else",
            }
        );

        await music.Received(1).PauseAsync(ChannelId.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nothing_was_playing_before_the_interruption_so_no_resume_is_issued()
    {
        PlayOnceResumeTracker tracker = new();
        tracker.Remember(
            ChannelId,
            new(
                InterruptingTrackUri,
                PriorTrackUri: null,
                PriorProgressMs: 0,
                PriorWasPlaying: false
            )
        );
        IMusicService music = Substitute.For<IMusicService>();
        PlayOnceResumeHandler handler = new(
            tracker,
            music,
            NullLogger<PlayOnceResumeHandler>.Instance
        );

        await handler.HandleAsync(
            new PlaybackStateChangedEvent
            {
                BroadcasterId = ChannelId,
                IsPlaying = true,
                TrackUri = "spotify:track:whatever-played-next",
            }
        );

        await music
            .DidNotReceive()
            .PlayTrackOnceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        tracker.TryPeek(ChannelId, out _).Should().BeFalse();
    }
}

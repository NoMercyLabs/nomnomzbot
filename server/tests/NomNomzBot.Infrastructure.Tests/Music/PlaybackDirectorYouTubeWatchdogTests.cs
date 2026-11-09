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
using NomNomzBot.Domain.Music.ValueObjects;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// A YouTube failure gives the music back to Spotify with no human action: a playing video must keep
/// reporting, and a Music Player page that goes away ends the video.
/// </summary>
public sealed partial class PlaybackDirectorTests
{
    private static readonly TimeSpan ReportSilence = TimeSpan.FromMilliseconds(15_001);

    /// <summary>The video took over from the owner's Spotify and plays; Spotify is paused for it.</summary>
    private static async Task<PlaybackItem> VideoHoldingSpotify(
        Rig rig,
        params PlaybackItem[] behind
    )
    {
        await rig.OwnerPlays(rig.Spotify, OwnTrack, 10_000);
        PlaybackItem video = Item("yt1", EngineKind.YouTube);
        await rig.Request(video);
        foreach (PlaybackItem item in behind)
            await rig.Request(item);
        await SpotifyMovesOn(rig, OwnTrack, OwnNextTrack);
        await YouTubeTakesOver(rig, OwnNextTrack, video);
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, video));

        return video;
    }

    private static readonly EngineCall[] TakeoverCalls = [new("Stop"), new("Seek", "0")];

    [Fact]
    public async Task Watchdog_AVideoSilentFor15Seconds_IsStoppedAndSpotifyResumes()
    {
        Rig rig = new();
        await VideoHoldingSpotify(rig);

        rig.Clock.Advance(ReportSilence);
        await rig.Director.CheckDeadlinesAsync();

        rig.YouTube.Calls.Should().Equal(new EngineCall("Start", "yt1"), new EngineCall("Stop"));
        rig.Spotify.Calls.Should().Equal([.. TakeoverCalls, new EngineCall("Resume")]);
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        Trail(rig).Should().ContainInOrder("youtube_report_timeout yt1", "spotify_resumed");
    }

    [Fact]
    public async Task Watchdog_EveryPageReportRenewsTheDeadline()
    {
        Rig rig = new();
        PlaybackItem video = await VideoHoldingSpotify(rig);

        for (int beat = 0; beat < 4; beat++)
        {
            rig.Clock.Advance(TimeSpan.FromSeconds(10));
            await rig.Plays(rig.YouTube, video);
            await rig.Director.CheckDeadlinesAsync();
        }

        rig.YouTube.Calls.Should().Equal(new EngineCall("Start", "yt1"));
        rig.Spotify.Calls.Should().Equal(TakeoverCalls);
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, video));
    }

    [Fact]
    public async Task Watchdog_APageReportArrivingLate_KeepsTheVideoItProvesAlive()
    {
        Rig rig = new();
        PlaybackItem video = await VideoHoldingSpotify(rig);

        rig.Clock.Advance(ReportSilence + TimeSpan.FromSeconds(1));
        await rig.Plays(rig.YouTube, video);

        rig.Spotify.Calls.Should().Equal(TakeoverCalls);
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, video));
    }

    [Fact]
    public async Task Watchdog_APageThatCannotBeReached_StillGivesSpotifyBack()
    {
        Rig rig = new();
        await VideoHoldingSpotify(rig);
        rig.YouTube.BlockCommands = EngineBlockReason.NoOutput;

        rig.Clock.Advance(ReportSilence);
        await rig.Director.CheckDeadlinesAsync();

        rig.Spotify.Calls.Should().Equal([.. TakeoverCalls, new EngineCall("Resume")]);
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
    }

    [Fact]
    public async Task Watchdog_AnotherVideoWaiting_NeverKeepsSpotifyPaused()
    {
        Rig rig = new();
        await VideoHoldingSpotify(rig, Item("yt2", EngineKind.YouTube, owner: "bob"));

        rig.Clock.Advance(ReportSilence);
        await rig.Director.CheckDeadlinesAsync();

        rig.Spotify.Calls.Should().Equal([.. TakeoverCalls, new EngineCall("Resume")]);
        rig.YouTube.Calls.Should().NotContain(new EngineCall("Start", "yt2"));
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        rig.QueueIds.Should().Equal("yt2");
    }

    [Fact]
    public async Task Watchdog_AVideoTakenOverByAnOwnerSkip_ResumesTheOwnersSpotify()
    {
        Rig rig = new();
        await rig.OwnerPlays(rig.Spotify, OwnTrack, 10_000);
        PlaybackItem video = Item("yt1", EngineKind.YouTube);
        await rig.Request(video);
        await rig.Director.HandleAsync(new OwnerSkip());
        await YouTubeTakesOver(rig, OwnTrack, video);

        rig.Clock.Advance(ReportSilence);
        await rig.Director.CheckDeadlinesAsync();

        rig.Spotify.Calls.Should().Equal(new EngineCall("Stop"), new EngineCall("Resume"));
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
    }

    [Fact]
    public async Task Watchdog_NoSpotifyHeld_MovesOnToTheNextRequest()
    {
        Rig rig = new();
        await rig.StartPlaying(EngineKind.YouTube);
        PlaybackItem next = Item("b", owner: "bob");
        await rig.Request(next);

        rig.Clock.Advance(ReportSilence);
        await rig.Director.CheckDeadlinesAsync();

        rig.YouTube.Calls.Should().Equal(new EngineCall("Start", "a"), new EngineCall("Stop"));
        rig.Spotify.Calls.Should().Equal(new EngineCall("Start", "b"));
        rig.Director.State.Should().Be(PlaybackState.Starting(EngineKind.Spotify, next));
    }

    [Fact]
    public async Task Watchdog_NoSpotifyHeldAndNothingWaiting_GoesIdle()
    {
        Rig rig = new();
        await rig.StartPlaying(EngineKind.YouTube);

        rig.Clock.Advance(ReportSilence);
        await rig.Director.CheckDeadlinesAsync();

        rig.YouTube.Calls.Should().Equal(new EngineCall("Start", "a"), new EngineCall("Stop"));
        rig.Spotify.Calls.Should().BeEmpty();
        rig.Director.State.Should().Be(PlaybackState.Idle);
    }

    [Fact]
    public async Task Watchdog_ADeadVideoIsNotTakenForTheOwnersMusic()
    {
        Rig rig = new();
        await rig.StartPlaying(EngineKind.YouTube);
        await rig.Request(Item("b", EngineKind.YouTube, owner: "bob"));

        rig.Clock.Advance(ReportSilence);
        await rig.Director.CheckDeadlinesAsync();
        rig.Clock.Advance(TimeSpan.FromSeconds(10));
        await rig.Director.CheckDeadlinesAsync();

        rig.Director.State.Should().Be(PlaybackState.Blocked("did not start"));
    }

    [Fact]
    public async Task Watchdog_NeverTouchesSpotifyOnlyPlayback()
    {
        Rig rig = new();
        PlaybackItem song = await rig.StartPlaying(EngineKind.Spotify);

        rig.Clock.Advance(TimeSpan.FromMinutes(3));
        await rig.Director.CheckDeadlinesAsync();

        rig.Spotify.Calls.Should().Equal(new EngineCall("Start", "a"));
        rig.YouTube.Calls.Should().BeEmpty();
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, song));
    }

    [Fact]
    public async Task Watchdog_AVideoNoQueueRowOwnsAndNoSpotifyHeld_IsLeftAlone()
    {
        Rig rig = new();
        await rig.OwnerPlays(rig.YouTube, "youtube:not-a-row", 1_000);

        rig.Clock.Advance(TimeSpan.FromMinutes(3));
        await rig.Director.CheckDeadlinesAsync();

        rig.YouTube.Calls.Should().BeEmpty();
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, null));
    }

    [Fact]
    public async Task OutputDetached_ThePageStaysGone_SpotifyResumesAfterTheReloadGrace()
    {
        Rig rig = new();
        await VideoHoldingSpotify(rig);

        await rig.Director.HandleAsync(new OutputDetached());
        rig.Spotify.Calls.Should().Equal(TakeoverCalls, "a reloading page gets a moment to return");

        rig.Clock.Advance(TimeSpan.FromMilliseconds(5_001));
        await rig.Director.CheckDeadlinesAsync();

        rig.Spotify.Calls.Should().Equal([.. TakeoverCalls, new EngineCall("Resume")]);
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        Trail(rig).Should().ContainInOrder("output_detached yt1", "youtube_output_lost yt1");
    }

    [Fact]
    public async Task OutputDetached_ThePageReloadsInTime_KeepsTheVideoAndTheFullDeadline()
    {
        Rig rig = new();
        PlaybackItem video = await VideoHoldingSpotify(rig);

        await rig.Director.HandleAsync(new OutputDetached());
        rig.Clock.Advance(TimeSpan.FromSeconds(3));
        await rig.Director.HandleAsync(new OutputAttached());
        rig.Clock.Advance(TimeSpan.FromSeconds(14));
        await rig.Director.CheckDeadlinesAsync();

        rig.YouTube.Calls.Should()
            .Equal(new EngineCall("Start", "yt1"), new EngineCall("Reattach", "False"));
        rig.Spotify.Calls.Should().Equal(TakeoverCalls);
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, video));
    }

    [Fact]
    public async Task OutputDetached_WhileSpotifyPlays_ChangesNothing()
    {
        Rig rig = new();
        PlaybackItem song = await rig.StartPlaying(EngineKind.Spotify);

        await rig.Director.HandleAsync(new OutputDetached());
        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        await rig.Director.CheckDeadlinesAsync();

        rig.Spotify.Calls.Should().Equal(new EngineCall("Start", "a"));
        rig.YouTube.Calls.Should().BeEmpty();
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, song));
    }

    [Fact]
    public async Task StartTimeout_OfAVideo_StopsItSoALateStartCannotCutIntoSpotify()
    {
        Rig rig = new();
        await rig.OwnerPlays(rig.Spotify, OwnTrack, 10_000);
        await rig.Request(Item("yt1", EngineKind.YouTube));
        await SpotifyMovesOn(rig, OwnTrack, OwnNextTrack);
        await rig.Paused(rig.Spotify, OwnNextTrack, 0);

        rig.Clock.Advance(TimeSpan.FromSeconds(10));
        await rig.Director.CheckDeadlinesAsync();

        rig.YouTube.Calls.Should().Equal(new EngineCall("Start", "yt1"), new EngineCall("Stop"));
        rig.Spotify.Calls.Should().Equal([.. TakeoverCalls, new EngineCall("Resume")]);
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        rig.QueueIds.Should().Equal("yt1");
    }

    [Fact]
    public async Task StartTimeout_OfASpotifyRequest_SendsSpotifyNoStop()
    {
        Rig rig = new();
        await rig.Request(Item("a"));

        rig.Clock.Advance(TimeSpan.FromSeconds(10));
        await rig.Director.CheckDeadlinesAsync();

        rig.Spotify.Calls.Should().Equal(new EngineCall("Start", "a"));
    }
}

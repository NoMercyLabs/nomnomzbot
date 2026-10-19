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
using NomNomzBot.Domain.Music.ValueObjects;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>Owner intent, owner-intent detection in the engines, and foreign-engine silencing.</summary>
public sealed partial class PlaybackDirectorTests
{
    private const string Mine = "spotify:track:mine";

    /// <summary>Spotify plays the owner's track mid-way and a YouTube request waits (rows 6, 7, 13).</summary>
    private static async Task<(Rig Rig, PlaybackItem Yt)> OwnerMusicWithYouTubeWaiting()
    {
        Rig rig = new();
        await rig.OwnerPlays(rig.Spotify, Mine, 100_000);
        PlaybackItem yt = Item("y1", EngineKind.YouTube);
        await rig.Request(yt);
        return (rig, yt);
    }

    [Fact]
    public async Task Row6_OwnerPausesSpotifyMusicWhileYouTubeWaits_YouTubeNeverStartsAcrossTenPolls()
    {
        (Rig rig, _) = await OwnerMusicWithYouTubeWaiting();

        await rig.Director.HandleAsync(new OwnerPause());
        int journaled = rig.Journal.Count;
        for (int poll = 0; poll < 10; poll++)
        {
            rig.Clock.Advance(TimeSpan.FromSeconds(3));
            await rig.Paused(rig.Spotify, Mine, 100_000);
        }

        rig.Director.State.Should().Be(PlaybackState.PausedByOwner(EngineKind.Spotify, null));
        rig.YouTube.Calls.Should().BeEmpty();
        rig.Spotify.Calls.Should().Equal(new EngineCall("Pause"));
        rig.QueueIds.Should().Equal("y1");
        rig.Journal.Should().HaveCount(journaled);
        rig.Journal.Last().Reason.Should().Be("owner_pause");
    }

    [Fact]
    public async Task Row7_PauseDetectedFromTheSpotifyReport_HoldsWithoutAnyEngineCall()
    {
        (Rig rig, _) = await OwnerMusicWithYouTubeWaiting();

        await rig.Paused(rig.Spotify, Mine, 100_000);
        for (int poll = 0; poll < 10; poll++)
            await rig.Paused(rig.Spotify, Mine, 100_000);

        rig.Director.State.Should().Be(PlaybackState.PausedByOwner(EngineKind.Spotify, null));
        rig.Spotify.Calls.Should().BeEmpty();
        rig.YouTube.Calls.Should().BeEmpty();
        rig.QueueIds.Should().Equal("y1");
        rig.Journal.Last().Reason.Should().Be("owner_pause_detected");
    }

    [Fact]
    public async Task PausedReportNearTheEndOfTheTrack_IsNotAnOwnerPause()
    {
        Rig rig = new();
        await rig.OwnerPlays(rig.Spotify, Mine, 100_000);

        await rig.Paused(rig.Spotify, Mine, 195_000);

        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
    }

    [Fact]
    public async Task PausedReportRightAfterThePauseWeSent_IsNotDetectedAgain()
    {
        Rig rig = new();
        await rig.OwnerPlays(rig.Spotify, Mine, 100_000);
        await rig.Director.HandleAsync(new OwnerPause());
        await rig.Director.HandleAsync(new OwnerResume());

        await rig.Paused(rig.Spotify, Mine, 100_000);

        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        rig.Spotify.Calls.Should().Equal(new EngineCall("Pause"), new EngineCall("Resume"));
    }

    [Fact]
    public async Task Row8_OwnerResumesInSpotifyWhilePaused_BecomesPlayingAndTheQueueIsUntouched()
    {
        (Rig rig, _) = await OwnerMusicWithYouTubeWaiting();
        await rig.Director.HandleAsync(new OwnerPause());
        rig.Clock.Advance(TimeSpan.FromSeconds(6));

        await rig.OwnerPlays(rig.Spotify, Mine, 106_000);

        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        rig.QueueIds.Should().Equal("y1");
        rig.Spotify.Calls.Should().Equal(new EngineCall("Pause"));
        rig.YouTube.Calls.Should().BeEmpty();
        rig.Journal.Last().Reason.Should().Be("owner_resume_detected");
    }

    [Fact]
    public async Task PlayingReportWithinFiveSecondsOfOurPause_IsAStalePollAndKeepsThePause()
    {
        (Rig rig, _) = await OwnerMusicWithYouTubeWaiting();
        await rig.Director.HandleAsync(new OwnerPause());
        rig.Clock.Advance(TimeSpan.FromSeconds(4.9));

        await rig.OwnerPlays(rig.Spotify, Mine, 100_000);

        rig.Director.State.Should().Be(PlaybackState.PausedByOwner(EngineKind.Spotify, null));
        rig.Spotify.Calls.Should().Equal(new EngineCall("Pause"));
    }

    [Fact]
    public async Task Row9_OwnerSkipsWhilePaused_TheHeadStartsOnItsEngine()
    {
        (Rig rig, PlaybackItem yt) = await OwnerMusicWithYouTubeWaiting();
        await rig.Director.HandleAsync(new OwnerPause());

        await rig.Director.HandleAsync(new OwnerSkip());

        rig.Director.State.Should().Be(PlaybackState.Starting(EngineKind.YouTube, yt));
        rig.YouTube.Calls.Should().Equal(new EngineCall("Start", "y1"));
        rig.Spotify.Calls.Should().Equal(new EngineCall("Pause"));
        rig.Journal.Last().Reason.Should().Be("owner_skip");
    }

    [Fact]
    public async Task PausedByOwner_RequestAdded_StaysPausedWithNoEngineCall()
    {
        (Rig rig, _) = await OwnerMusicWithYouTubeWaiting();
        await rig.Director.HandleAsync(new OwnerPause());

        await rig.Request(Item("y2", EngineKind.YouTube, owner: "bob"));

        rig.Director.State.Should().Be(PlaybackState.PausedByOwner(EngineKind.Spotify, null));
        rig.YouTube.Calls.Should().BeEmpty();
        rig.Spotify.Calls.Should().Equal(new EngineCall("Pause"));
        rig.Journal.Last().Reason.Should().Be("held_paused");
    }

    [Theory]
    [InlineData(EngineKind.Spotify)]
    [InlineData(EngineKind.YouTube)]
    public async Task PausedByOwner_SeekVolumeAndMute_AdjustThePausedEngineAndStayPaused(
        EngineKind kind
    )
    {
        Rig rig = new();
        PlaybackItem a = await rig.StartPlaying(kind);
        await rig.Director.HandleAsync(new OwnerPause());

        await rig.Director.HandleAsync(new OwnerSeek(12_000));
        await rig.Director.HandleAsync(new OwnerVolume(25));
        await rig.Director.HandleAsync(new OwnerMute(true));

        rig.Director.State.Should().Be(PlaybackState.PausedByOwner(kind, a));
        rig.Engine(kind)
            .Calls.Should()
            .Equal(
                new EngineCall("Start", "a"),
                new EngineCall("Pause"),
                new EngineCall("Seek", "12000"),
                new EngineCall("SetVolume", "25"),
                new EngineCall("SetVolume", "0")
            );
        rig.Other(kind).Calls.Should().BeEmpty();
        rig.Journal.Last().Reason.Should().Be("mute");
    }

    [Fact]
    public async Task Row10_OwnerPausesInTheYouTubeWidget_PausedByOwnerAndSpotifyIsNeverCalled()
    {
        Rig rig = new();
        PlaybackItem a = await rig.StartPlaying(EngineKind.YouTube);

        await rig.Paused(rig.YouTube, a.Uri, 50_000);

        rig.Director.State.Should().Be(PlaybackState.PausedByOwner(EngineKind.YouTube, a));
        rig.Spotify.Calls.Should().BeEmpty();
        rig.YouTube.Calls.Should().Equal(new EngineCall("Start", "a"));
        rig.Journal.Last().Reason.Should().Be("owner_pause_detected");
    }

    [Fact]
    public async Task Row11_SpotifyStartsOnItsOwnWhileYouTubePlays_SpotifyIsPausedAndJournaled()
    {
        Rig rig = new();
        PlaybackItem a = await rig.StartPlaying(EngineKind.YouTube);

        await rig.OwnerPlays(rig.Spotify, Mine, 1_000);

        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, a));
        rig.Spotify.Calls.Should().Equal(new EngineCall("Pause"));
        rig.YouTube.Calls.Should().Equal(new EngineCall("Start", "a"));
        rig.Journal.Last().Reason.Should().Be("foreign_engine_silenced");
    }

    [Fact]
    public async Task ForeignEngineStartsWhilePausedByOwner_IsSilencedAndTheStateStays()
    {
        Rig rig = new();
        PlaybackItem a = await rig.StartPlaying(EngineKind.YouTube);
        await rig.Director.HandleAsync(new OwnerPause());

        await rig.OwnerPlays(rig.Spotify, Mine, 1_000);

        rig.Director.State.Should().Be(PlaybackState.PausedByOwner(EngineKind.YouTube, a));
        rig.Spotify.Calls.Should().Equal(new EngineCall("Pause"));
        rig.Journal.Last().Reason.Should().Be("foreign_engine_silenced");
    }

    [Fact]
    public async Task Row13Proper_SpotifyPausedByOwnerBeforeTheYouTubeItem_NoResumeWhenItEnds()
    {
        (Rig rig, PlaybackItem yt) = await OwnerMusicWithYouTubeWaiting();
        await rig.Director.HandleAsync(new OwnerPause());
        await rig.Director.HandleAsync(new OwnerSkip());
        await rig.Plays(rig.YouTube, yt);

        await rig.Ends(rig.YouTube, yt);

        rig.Spotify.Calls.Should().Equal(new EngineCall("Pause"));
        rig.Director.State.Should().Be(PlaybackState.Idle);
        rig.QueueIds.Should().BeEmpty();
        rig.Journal.Last().Reason.Should().Be("queue_empty");
    }

    [Fact]
    public async Task Row5_OwnerResumeWhileYouTubePlays_NoSpotifyCallAndNoStateChange()
    {
        Rig rig = new();
        PlaybackItem a = await rig.StartPlaying(EngineKind.YouTube);

        await rig.Director.HandleAsync(new OwnerResume());

        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, a));
        rig.Spotify.Calls.Should().BeEmpty();
        rig.YouTube.Calls.Should().Equal(new EngineCall("Start", "a"));
    }

    [Theory]
    [InlineData(EngineKind.Spotify)]
    [InlineData(EngineKind.YouTube)]
    public async Task OwnerPause_PausesOnlyThePlayingEngine(EngineKind kind)
    {
        Rig rig = new();
        PlaybackItem a = await rig.StartPlaying(kind);

        await rig.Director.HandleAsync(new OwnerPause());

        rig.Director.State.Should().Be(PlaybackState.PausedByOwner(kind, a));
        rig.Engine(kind)
            .Calls.Should()
            .Equal(new EngineCall("Start", "a"), new EngineCall("Pause"));
        rig.Other(kind).Calls.Should().BeEmpty();
        rig.Journal.Last().Reason.Should().Be("owner_pause");
    }

    [Theory]
    [InlineData(EngineKind.Spotify)]
    [InlineData(EngineKind.YouTube)]
    public async Task OwnerResume_ResumesOnlyTheEngineOfTheState(EngineKind kind)
    {
        Rig rig = new();
        PlaybackItem a = await rig.StartPlaying(kind);
        await rig.Director.HandleAsync(new OwnerPause());

        await rig.Director.HandleAsync(new OwnerResume());

        rig.Director.State.Should().Be(PlaybackState.Playing(kind, a));
        rig.Engine(kind)
            .Calls.Should()
            .Equal(new EngineCall("Start", "a"), new EngineCall("Pause"), new EngineCall("Resume"));
        rig.Other(kind).Calls.Should().BeEmpty();
        rig.Journal.Last().Reason.Should().Be("owner_resume");
    }

    [Theory]
    [InlineData(EngineKind.Spotify)]
    [InlineData(EngineKind.YouTube)]
    public async Task OwnerSkip_StopsTheEngineWaitsForItsPausedReportThenStartsTheNextItem(
        EngineKind kind
    )
    {
        Rig rig = new();
        PlaybackItem a = await rig.StartPlaying(kind);
        PlaybackItem b = Item("b", kind, owner: "bob");
        rig.Queue.Enqueue(b);

        await rig.Director.HandleAsync(new OwnerSkip());

        rig.Director.State.Should().Be(PlaybackState.Starting(kind, b));
        rig.Engine(kind).Calls.Should().Equal(new EngineCall("Start", "a"), new EngineCall("Stop"));

        await rig.Paused(rig.Engine(kind), a.Uri, 1_000);

        rig.Engine(kind)
            .Calls.Should()
            .Equal(
                new EngineCall("Start", "a"),
                new EngineCall("Stop"),
                new EngineCall("Start", "b")
            );
        rig.Other(kind).Calls.Should().BeEmpty();
        rig.Journal.Select(e => e.Reason).Should().EndWith(["owner_skip", "stop_confirmed"]);
    }

    [Fact]
    public async Task OwnerSkip_WithoutAPausedReport_StartsTheNextItemAfterTwoSecondsAsStopUnconfirmed()
    {
        Rig rig = new();
        await rig.StartPlaying(EngineKind.Spotify);
        PlaybackItem b = Item("b", EngineKind.YouTube, owner: "bob");
        rig.Queue.Enqueue(b);
        await rig.Director.HandleAsync(new OwnerSkip());

        rig.Clock.Advance(TimeSpan.FromSeconds(2));
        await rig.Director.CheckDeadlinesAsync();

        rig.YouTube.Calls.Should().Equal(new EngineCall("Start", "b"));
        rig.Journal.Last().Reason.Should().Be("stop_unconfirmed");
    }

    [Fact]
    public async Task OwnerSkip_WithAnEmptyQueue_StopsTheEngineAndGoesIdle()
    {
        Rig rig = new();
        await rig.StartPlaying(EngineKind.YouTube);

        await rig.Director.HandleAsync(new OwnerSkip());

        rig.Director.State.Should().Be(PlaybackState.Idle);
        rig.YouTube.Calls.Should().Equal(new EngineCall("Start", "a"), new EngineCall("Stop"));
        rig.Spotify.Calls.Should().BeEmpty();
        rig.Journal.Last().Reason.Should().Be("queue_empty");
    }

    [Theory]
    [InlineData(EngineKind.Spotify)]
    [InlineData(EngineKind.YouTube)]
    public async Task OwnerSeek_SeeksOnlyThePlayingEngine(EngineKind kind)
    {
        Rig rig = new();
        PlaybackItem a = await rig.StartPlaying(kind);

        await rig.Director.HandleAsync(new OwnerSeek(42_000));

        rig.Director.State.Should().Be(PlaybackState.Playing(kind, a));
        rig.Engine(kind)
            .Calls.Should()
            .Equal(new EngineCall("Start", "a"), new EngineCall("Seek", "42000"));
        rig.Other(kind).Calls.Should().BeEmpty();
        rig.Journal.Last().Reason.Should().Be("seek");
    }

    [Theory]
    [InlineData(EngineKind.Spotify)]
    [InlineData(EngineKind.YouTube)]
    public async Task OwnerVolume_SetsTheVolumeOnlyOnThePlayingEngine(EngineKind kind)
    {
        Rig rig = new();
        PlaybackItem a = await rig.StartPlaying(kind);

        await rig.Director.HandleAsync(new OwnerVolume(30));

        rig.Director.State.Should().Be(PlaybackState.Playing(kind, a));
        rig.Engine(kind)
            .Calls.Should()
            .Equal(new EngineCall("Start", "a"), new EngineCall("SetVolume", "30"));
        rig.Other(kind).Calls.Should().BeEmpty();
        rig.Journal.Last().Reason.Should().Be("volume");
    }

    [Theory]
    [InlineData(EngineKind.Spotify)]
    [InlineData(EngineKind.YouTube)]
    public async Task OwnerMute_SilencesTheEngineAndUnmuteRestoresTheOwnerLevelEvenAfterAVolumeChange(
        EngineKind kind
    )
    {
        Rig rig = new();
        await rig.StartPlaying(kind);
        await rig.Director.HandleAsync(new OwnerVolume(70));

        await rig.Director.HandleAsync(new OwnerMute(true));
        await rig.Director.HandleAsync(new OwnerVolume(40));
        await rig.Director.HandleAsync(new OwnerMute(false));

        rig.Engine(kind)
            .Calls.Skip(1)
            .Should()
            .Equal(
                new EngineCall("SetVolume", "70"),
                new EngineCall("SetVolume", "0"),
                new EngineCall("SetVolume", "40")
            );
        rig.Other(kind).Calls.Should().BeEmpty();
        rig.Journal.Select(e => e.Reason).TakeLast(3).Should().Equal("mute", "volume", "unmute");
    }

    [Fact]
    public async Task Row22_PreviousAfterThreeSeconds_SeeksToTheStartOnly()
    {
        Rig rig = new();
        PlaybackItem a = await rig.StartPlaying(EngineKind.Spotify);
        await rig.OwnerPlays(rig.Spotify, a.Uri, 10_000);

        await rig.Director.HandleAsync(new OwnerPrevious());

        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, a));
        rig.Spotify.Calls.Should().Equal(new EngineCall("Start", "a"), new EngineCall("Seek", "0"));
        rig.Journal.Last().Reason.Should().Be("previous_restart");
    }

    [Theory]
    [InlineData(EngineKind.Spotify)]
    [InlineData(EngineKind.YouTube)]
    public async Task Row22_PreviousWithinThreeSeconds_PutsTheLastPlayedItemAtTheHeadAndStartsIt(
        EngineKind kind
    )
    {
        Rig rig = new();
        PlaybackItem a = await rig.StartPlaying(kind);
        PlaybackItem b = Item("b", kind, owner: "bob");
        rig.Queue.Enqueue(b);
        await rig.Ends(rig.Engine(kind), a);
        await rig.Plays(rig.Engine(kind), b);

        await rig.Director.HandleAsync(new OwnerPrevious());

        rig.Director.State.Should().Be(PlaybackState.Starting(kind, a));
        rig.QueueIds.Should().Equal("a", "b");
        rig.Engine(kind).Calls.Last().Should().Be(new EngineCall("Stop"));

        await rig.Paused(rig.Engine(kind), b.Uri, 1_000);

        rig.Engine(kind).Calls.Last().Should().Be(new EngineCall("Start", "a"));
        rig.Other(kind).Calls.Should().BeEmpty();
        rig.Journal.Select(e => e.Reason).Should().EndWith(["previous_item", "stop_confirmed"]);
    }

    [Fact]
    public async Task Previous_WithNothingPlayedBefore_SeeksToTheStartAndKeepsTheItem()
    {
        Rig rig = new();
        PlaybackItem a = await rig.StartPlaying(EngineKind.YouTube);

        await rig.Director.HandleAsync(new OwnerPrevious());

        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, a));
        rig.YouTube.Calls.Should().Equal(new EngineCall("Start", "a"), new EngineCall("Seek", "0"));
        rig.QueueIds.Should().BeEmpty();
    }
}

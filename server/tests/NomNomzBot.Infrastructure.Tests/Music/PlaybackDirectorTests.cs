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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music.Playback;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

public sealed partial class PlaybackDirectorTests
{
    private sealed class Rig
    {
        public Rig()
        {
            Clock = NewClock();
            Director = new(Channel, [Spotify, YouTube], Queue, Clock, Bus);
        }

        public FakeEngine Spotify { get; } = new(EngineKind.Spotify);

        public FakeEngine YouTube { get; } = new(EngineKind.YouTube);

        public FairPlaybackQueue Queue { get; } = new();

        public RecordingBus Bus { get; } = new();

        public FakeTimeProvider Clock { get; }

        public PlaybackDirector Director { get; }

        public List<PlaybackTransitionedEvent> Journal =>
            [.. Bus.Published.OfType<PlaybackTransitionedEvent>()];

        public List<string> QueueIds => [.. Queue.Snapshot.Select(i => i.Id)];

        public async Task Request(PlaybackItem item)
        {
            Queue.Enqueue(item);
            await Director.HandleAsync(new RequestAdded());
        }

        public Task Plays(FakeEngine engine, PlaybackItem item) =>
            engine.EmitAsync(new EngineReport(engine.Kind, item.Uri, true, 1_000, item.DurationMs));

        public FakeEngine Engine(EngineKind kind) => kind == EngineKind.Spotify ? Spotify : YouTube;

        public FakeEngine Other(EngineKind kind) => kind == EngineKind.Spotify ? YouTube : Spotify;

        public Task Paused(
            FakeEngine engine,
            string uri,
            int progressMs,
            int durationMs = 200_000
        ) => engine.EmitAsync(new EngineReport(engine.Kind, uri, false, progressMs, durationMs));

        /// <summary>Requests an item on the engine and lets that engine report it as playing.</summary>
        public async Task<PlaybackItem> StartPlaying(EngineKind kind, string id = "a")
        {
            PlaybackItem item = Item(id, kind, owner: $"owner-{id}");
            await Request(item);
            await Plays(Engine(kind), item);
            return item;
        }

        public Task Ends(FakeEngine engine, PlaybackItem item) =>
            engine.EmitAsync(new TrackEnded(engine.Kind, item.Uri));

        public Task OwnerPlays(
            FakeEngine engine,
            string uri,
            int progressMs,
            int durationMs = 200_000
        ) => engine.EmitAsync(new EngineReport(engine.Kind, uri, true, progressMs, durationMs));
    }

    [Fact]
    public async Task Row1_ReportOfQueuedNonHeadItem_RemovesOnlyThatRow()
    {
        Rig rig = new();
        List<PlaybackItem> items = [];
        for (int n = 1; n <= 16; n++)
            items.Add(Item($"i{n}", owner: $"user{n}"));
        foreach (PlaybackItem item in items)
            rig.Queue.Enqueue(item);

        await rig.Plays(rig.Spotify, items[15]);

        rig.QueueIds.Should().Equal(Enumerable.Range(1, 15).Select(n => $"i{n}"));
        rig.Director.State.Kind.Should().Be(PlaybackStateKind.Playing);
        rig.Director.State.Engine.Should().Be(EngineKind.Spotify);
        rig.Director.State.Item!.Id.Should().Be("i16");
        rig.Director.LastPlayed!.Id.Should().Be("i16");
        rig.Spotify.Calls.Should().BeEmpty();
        rig.YouTube.Calls.Should().BeEmpty();
        PlaybackTransitionedEvent only = rig.Journal.Should().ContainSingle().Subject;
        only.Reason.Should().Be("played_out_of_order");
        only.QueueItemId.Should().Be("i16");
        only.BroadcasterId.Should().Be(Channel);
    }

    [Fact]
    public async Task Row2_IdleWithHead_StartsTheHeadOnItsEngineExactlyOnce()
    {
        Rig rig = new();
        PlaybackItem head = Item("a");

        await rig.Request(head);

        rig.Spotify.Calls.Should().Equal(new EngineCall("Start", "a"));
        rig.YouTube.Calls.Should().BeEmpty();
        rig.Director.State.Should().Be(PlaybackState.Starting(EngineKind.Spotify, head));
        rig.QueueIds.Should().Equal("a");
        PlaybackTransitionedEvent only = rig.Journal.Should().ContainSingle().Subject;
        (only.From, only.To, only.Input, only.Reason)
            .Should()
            .Be(("Idle", "Starting", "RequestAdded", "start_head"));
    }

    [Fact]
    public async Task Starting_ThenEngineReportsTheItem_PlaysItAndRemovesOnlyThatRow()
    {
        Rig rig = new();
        PlaybackItem a = Item("a", owner: "alice");
        PlaybackItem b = Item("b", owner: "bob");
        await rig.Request(a);
        rig.Queue.Enqueue(b);

        await rig.Plays(rig.Spotify, a);

        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, a));
        rig.QueueIds.Should().Equal("b");
        rig.Director.LastPlayed!.Id.Should().Be("a");
        rig.Spotify.Calls.Should().Equal(new EngineCall("Start", "a"));
        rig.Journal.Select(e => e.Reason).Should().Equal("start_head", "started");
    }

    [Fact]
    public async Task Row16_StartSentAndNoReportIn10Seconds_BlocksAndKeepsTheHead()
    {
        Rig rig = new();
        PlaybackItem a = Item("a");
        await rig.Request(a);

        rig.Clock.Advance(TimeSpan.FromSeconds(9.9));
        await rig.Director.CheckDeadlinesAsync();
        rig.Director.State.Kind.Should().Be(PlaybackStateKind.Starting);

        rig.Clock.Advance(TimeSpan.FromSeconds(0.1));
        await rig.Director.CheckDeadlinesAsync();

        rig.Director.State.Should().Be(PlaybackState.Blocked("did not start"));
        rig.QueueIds.Should().Equal("a");
        rig.Spotify.Calls.Should().Equal(new EngineCall("Start", "a"));
        rig.Journal.Last().Reason.Should().Be("start_timeout");
        rig.Director.LastPlayed.Should().BeNull();
    }

    [Fact]
    public async Task Starting_WhenTheEngineRefusesTheStart_BlocksWithItsReasonAndKeepsTheHead()
    {
        Rig rig = new();
        rig.Spotify.StartResult = EngineStartResult.Refused("token dead");

        await rig.Request(Item("a"));

        rig.Director.State.Should().Be(PlaybackState.Blocked("token dead"));
        rig.QueueIds.Should().Equal("a");
    }

    [Fact]
    public async Task Row17_OwnerPicksANonQueueTrackWhileAnItemPlays_BecomesOwnerMusicAndKeepsTheHead()
    {
        Rig rig = new();
        PlaybackItem a = Item("a", owner: "alice");
        PlaybackItem b = Item("b", owner: "bob");
        await rig.Request(a);
        rig.Queue.Enqueue(b);
        await rig.Plays(rig.Spotify, a);

        await rig.OwnerPlays(rig.Spotify, "spotify:track:owner-pick", 500);

        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        rig.QueueIds.Should().Equal("b");
        rig.Spotify.Calls.Should().Equal(new EngineCall("Start", "a"));
        rig.Journal.Last().Reason.Should().Be("owner_music");
    }

    [Fact]
    public async Task Idle_OwnerMusicPlays_BecomesPlayingWithoutAnItem()
    {
        Rig rig = new();

        await rig.OwnerPlays(rig.Spotify, "spotify:track:mine", 10_000);

        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        rig.Spotify.Calls.Should().BeEmpty();
        rig.Journal.Should().ContainSingle().Which.Reason.Should().Be("owner_music");
    }

    [Fact]
    public async Task OwnerMusicReportThatChangesNothing_IsNotJournaled()
    {
        Rig rig = new();
        await rig.OwnerPlays(rig.Spotify, "spotify:track:mine", 10_000);

        await rig.OwnerPlays(rig.Spotify, "spotify:track:mine", 15_000);
        await rig.OwnerPlays(rig.Spotify, "spotify:track:mine", 20_000);

        rig.Journal.Should().ContainSingle();
    }

    [Fact]
    public async Task PlayingOwnerMusic_RequestBeforeTheNearEndWindow_WaitsThenHandsOverAtSixSeconds()
    {
        Rig rig = new();
        await rig.OwnerPlays(rig.Spotify, "spotify:track:mine", 100_000);
        PlaybackItem head = Item("a");

        await rig.Request(head);

        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        rig.Spotify.Calls.Should().BeEmpty();
        rig.Journal.Last().Reason.Should().Be("waiting_near_end");

        await rig.OwnerPlays(rig.Spotify, "spotify:track:mine", 193_999);
        rig.Spotify.Calls.Should().BeEmpty();

        await rig.OwnerPlays(rig.Spotify, "spotify:track:mine", 194_000);

        rig.Director.State.Should().Be(PlaybackState.Starting(EngineKind.Spotify, head));
        rig.Spotify.Calls.Should().Equal(new EngineCall("Start", "a"));
        rig.Journal.Last().Reason.Should().Be("hand_over");
        rig.QueueIds.Should().Equal("a");
    }

    [Fact]
    public async Task Row12_YouTubeItemEndsWithEmptyQueue_ResumesSpotifyOwnerMusicExactlyOnce()
    {
        Rig rig = new();
        await rig.OwnerPlays(rig.Spotify, "spotify:track:mine", 195_000);
        PlaybackItem yt = Item("y1", EngineKind.YouTube);
        await rig.Request(yt);
        rig.Spotify.Calls.Should().Equal(new EngineCall("Stop"));
        rig.YouTube.Calls.Should().BeEmpty();
        await rig.Spotify.EmitAsync(
            new EngineReport(EngineKind.Spotify, "spotify:track:mine", false, 195_500, 200_000)
        );
        await rig.Plays(rig.YouTube, yt);

        await rig.Ends(rig.YouTube, yt);

        rig.Spotify.Calls.Should().Equal(new EngineCall("Stop"), new EngineCall("Resume"));
        rig.YouTube.Calls.Should().Equal(new EngineCall("Start", "y1"));
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        rig.QueueIds.Should().BeEmpty();
        rig.Journal.Last().Reason.Should().Be("owner_music_resumed");

        await rig.Ends(rig.YouTube, yt);
        rig.Spotify.Calls.Count(c => c.Name == "Resume").Should().Be(1);
    }

    [Fact]
    public async Task Row13_SpotifyOwnerMusicWasPaused_NoResumeAfterTheYouTubeItemEnds()
    {
        Rig rig = new();
        await rig.Spotify.EmitAsync(
            new EngineReport(EngineKind.Spotify, "spotify:track:mine", true, 100_000, 200_000)
        );
        await rig.Spotify.EmitAsync(
            new EngineReport(EngineKind.Spotify, "spotify:track:mine", false, 195_000, 200_000)
        );
        PlaybackItem yt = Item("y1", EngineKind.YouTube);
        await rig.Request(yt);
        await rig.Plays(rig.YouTube, yt);

        await rig.Ends(rig.YouTube, yt);

        rig.Spotify.Calls.Should().NotContain(c => c.Name == "Resume");
        rig.Director.State.Should().Be(PlaybackState.Idle);
        rig.Journal.Last().Reason.Should().Be("queue_empty");
    }

    [Fact]
    public async Task TrackEndedWithEmptyQueueAndNoOwnerMusic_GoesIdleWithNoEngineCall()
    {
        Rig rig = new();
        PlaybackItem a = Item("a");
        await rig.Request(a);
        await rig.Plays(rig.Spotify, a);

        await rig.Ends(rig.Spotify, a);

        rig.Director.State.Should().Be(PlaybackState.Idle);
        rig.Spotify.Calls.Should().Equal(new EngineCall("Start", "a"));
        rig.YouTube.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Row23_EngineSwitchAtTrackEnd_StartsTheNextEngineAtOnceWithoutStopOrDelay()
    {
        Rig rig = new();
        PlaybackItem a = Item("A", EngineKind.Spotify, "user1");
        PlaybackItem b = Item("B", EngineKind.YouTube, "user2");
        PlaybackItem c = Item("C", EngineKind.Spotify, "user1");
        rig.Queue.Enqueue(a);
        rig.Queue.Enqueue(c);
        rig.Queue.Enqueue(b);
        rig.QueueIds.Should().Equal("A", "B", "C");
        await rig.Director.HandleAsync(new RequestAdded());
        await rig.Plays(rig.Spotify, a);

        await rig.Ends(rig.Spotify, a);

        rig.YouTube.Calls.Should().Equal(new EngineCall("Start", "B"));
        rig.Spotify.Calls.Should().Equal(new EngineCall("Start", "A"));
        rig.Director.State.Should().Be(PlaybackState.Starting(EngineKind.YouTube, b));

        await rig.Plays(rig.YouTube, b);
        await rig.Ends(rig.YouTube, b);

        rig.Spotify.Calls.Should()
            .Equal(new EngineCall("Start", "A"), new EngineCall("Start", "C"));
        rig.YouTube.Calls.Should().Equal(new EngineCall("Start", "B"));
        rig.Director.State.Should().Be(PlaybackState.Starting(EngineKind.Spotify, c));
        rig.Journal.Select(e => e.Reason)
            .Should()
            .Equal("start_head", "started", "next_item", "started", "next_item");
    }

    [Fact]
    public async Task Row24_StopNotConfirmed_StartsAfterTwoSecondsAndJournalsStopUnconfirmed()
    {
        Rig rig = new();
        await rig.OwnerPlays(rig.Spotify, "spotify:track:mine", 195_000);
        PlaybackItem yt = Item("y1", EngineKind.YouTube);

        await rig.Request(yt);

        rig.Spotify.Calls.Should().Equal(new EngineCall("Stop"));
        rig.YouTube.Calls.Should().BeEmpty();
        rig.Director.State.Should().Be(PlaybackState.Starting(EngineKind.YouTube, yt));

        rig.Clock.Advance(TimeSpan.FromSeconds(1.9));
        await rig.Director.CheckDeadlinesAsync();
        rig.YouTube.Calls.Should().BeEmpty();

        rig.Clock.Advance(TimeSpan.FromSeconds(0.1));
        await rig.Director.CheckDeadlinesAsync();

        rig.YouTube.Calls.Should().Equal(new EngineCall("Start", "y1"));
        rig.Spotify.Calls.Should().Equal(new EngineCall("Stop"));
        rig.Journal.Select(e => e.Reason).Should().Contain("stop_unconfirmed");
        rig.Director.State.Should().Be(PlaybackState.Starting(EngineKind.YouTube, yt));

        rig.Clock.Advance(TimeSpan.FromSeconds(10));
        await rig.Director.CheckDeadlinesAsync();
        rig.Director.State.Should().Be(PlaybackState.Blocked("did not start"));
    }

    [Fact]
    public async Task EngineSwitch_OldEngineConfirmsPaused_StartsAtOnceWithoutStopUnconfirmed()
    {
        Rig rig = new();
        await rig.OwnerPlays(rig.Spotify, "spotify:track:mine", 195_000);
        PlaybackItem yt = Item("y1", EngineKind.YouTube);
        await rig.Request(yt);

        await rig.Spotify.EmitAsync(
            new EngineReport(EngineKind.Spotify, "spotify:track:mine", false, 195_100, 200_000)
        );

        rig.YouTube.Calls.Should().Equal(new EngineCall("Start", "y1"));
        rig.Journal.Select(e => e.Reason).Should().NotContain("stop_unconfirmed");
        rig.Journal.Last().Reason.Should().Be("stop_confirmed");
    }

    [Fact]
    public async Task EngineSwitchStopWait_ReportsFromTheStoppingEngineThatStillPlay_AreIgnored()
    {
        Rig rig = new();
        await rig.OwnerPlays(rig.Spotify, "spotify:track:mine", 195_000);
        await rig.Request(Item("y1", EngineKind.YouTube));
        int journaled = rig.Journal.Count;

        await rig.OwnerPlays(rig.Spotify, "spotify:track:mine", 196_000);

        rig.YouTube.Calls.Should().BeEmpty();
        rig.Journal.Should().HaveCount(journaled);
    }

    [Fact]
    public async Task Blocked_OutputAttached_GoesIdleAndStartsTheKeptHeadAgain()
    {
        Rig rig = new();
        PlaybackItem a = Item("a");
        await rig.Request(a);
        rig.Clock.Advance(TimeSpan.FromSeconds(10));
        await rig.Director.CheckDeadlinesAsync();
        rig.Director.State.Kind.Should().Be(PlaybackStateKind.Blocked);

        await rig.Director.HandleAsync(new OutputAttached());

        rig.Spotify.Calls.Should()
            .Equal(new EngineCall("Start", "a"), new EngineCall("Start", "a"));
        rig.Director.State.Should().Be(PlaybackState.Starting(EngineKind.Spotify, a));
        rig.Journal.Select(e => (e.From, e.To, e.Reason))
            .Should()
            .Equal(
                ("Idle", "Starting", "start_head"),
                ("Starting", "Blocked", "start_timeout"),
                ("Blocked", "Idle", "output_attached"),
                ("Idle", "Starting", "start_head")
            );
    }

    [Fact]
    public async Task Starting_OwnerIntentInputs_AreIgnoredAndJournaled_WithNoEngineCall()
    {
        Rig rig = new();
        PlaybackItem a = Item("a");
        await rig.Request(a);

        await rig.Director.HandleAsync(new OwnerSkip());
        await rig.Director.HandleAsync(new OwnerPause());
        await rig.Director.HandleAsync(new OwnerVolume(30));

        rig.Director.State.Should().Be(PlaybackState.Starting(EngineKind.Spotify, a));
        rig.Spotify.Calls.Should().Equal(new EngineCall("Start", "a"));
        rig.Journal.Skip(1)
            .Select(e => (e.Input, e.Reason))
            .Should()
            .Equal(("OwnerSkip", "ignored"), ("OwnerPause", "ignored"), ("OwnerVolume", "ignored"));
    }
}

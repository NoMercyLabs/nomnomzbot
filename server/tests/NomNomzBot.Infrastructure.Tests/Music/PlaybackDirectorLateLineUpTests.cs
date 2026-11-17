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
using NomNomzBot.Infrastructure.Music;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

// Stoney, 2026-11-17: "the fair queue system really needs to enqueue the next song later, almost at the end of the
// current song so that other people can queue their slot without having to do the whole juggling of removing it
// and requeueing the person who got moved down".
public sealed partial class PlaybackDirectorTests
{
    [Fact]
    public async Task The_next_request_stays_in_the_fair_queue_while_most_of_the_song_is_left()
    {
        Rig rig = new();
        PlaybackItem a = Item("a", owner: "alice");
        PlaybackItem b = Item("b", owner: "bob");
        await rig.Request(a);
        rig.Queue.Enqueue(b);

        await rig.Plays(rig.Spotify, a);
        rig.Clock.Advance(TimeSpan.FromSeconds(100));
        await rig.Director.CheckDeadlinesAsync();

        rig.Spotify.Calls.Should().Equal(new EngineCall("Start", "a"));
        rig.Journal.Select(e => e.Reason).Should().Equal("start_head", "started");
        rig.QueueIds.Should().Equal("b");
    }

    [Fact]
    public async Task The_next_request_goes_into_Spotify_once_the_song_nears_its_end()
    {
        Rig rig = new();
        PlaybackItem a = Item("a", owner: "alice");
        PlaybackItem b = Item("b", owner: "bob");
        await rig.Request(a);
        rig.Queue.Enqueue(b);

        await rig.Plays(rig.Spotify, a);
        // a reported 1 s in of 200 s: 15 s is left at 185 s.
        rig.Clock.Advance(TimeSpan.FromSeconds(183.9));
        await rig.Director.CheckDeadlinesAsync();
        rig.Spotify.Calls.Should().Equal(new EngineCall("Start", "a"));

        rig.Clock.Advance(TimeSpan.FromSeconds(0.2));
        await rig.Director.CheckDeadlinesAsync();

        rig.Spotify.Calls.Should()
            .Equal(new EngineCall("Start", "a"), new EngineCall("QueueNext", "b"));
        rig.Journal.Select(e => e.Reason).Should().Equal("start_head", "started", "lined_up");
    }

    [Fact]
    public async Task A_request_that_ranks_higher_before_the_end_is_the_one_lined_up_and_nothing_is_juggled()
    {
        FairQueue<PlaybackItem> fair = new();
        Rig rig = new(fair);
        PlaybackItem a = Item("a", owner: "alice");
        await rig.Request(a);
        await rig.Plays(rig.Spotify, a);
        // Alice's song plays, so her next request ranks 2.
        fair.SetNowPlayingOwner("alice");
        rig.Queue.Enqueue(Item("a2", owner: "alice"));

        rig.Clock.Advance(TimeSpan.FromSeconds(60));
        await rig.Request(Item("c", owner: "carol"));
        rig.Clock.Advance(TimeSpan.FromSeconds(125));
        await rig.Director.CheckDeadlinesAsync();

        rig.QueueIds.Should().Equal("c", "a2");
        rig.Spotify.Calls.Should()
            .Equal(new EngineCall("Start", "a"), new EngineCall("QueueNext", "c"));
        rig.Journal.Select(e => e.Reason).Should().NotContain("fair_order_first");
    }

    [Fact]
    public async Task A_song_with_no_known_length_lines_up_the_next_request_at_once()
    {
        Rig rig = new();
        PlaybackItem a = Item("a", owner: "alice", durationMs: 0);
        PlaybackItem b = Item("b", owner: "bob");
        await rig.Request(a);
        rig.Queue.Enqueue(b);

        await rig.Plays(rig.Spotify, a);

        rig.Spotify.Calls.Should()
            .Equal(new EngineCall("Start", "a"), new EngineCall("QueueNext", "b"));
    }
}

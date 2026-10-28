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
/// The poller sends RequestAdded on every tick (audit 2026-10-19: 72% of journal rows were these no-op ticks).
/// A RequestAdded that changes neither the state nor the queue is not journaled; a real new request still is.
/// </summary>
public sealed partial class PlaybackDirectorTests
{
    [Fact]
    public async Task PollTick_WithNothingWaiting_IsNotJournaled()
    {
        Rig rig = new();

        await rig.Director.HandleAsync(new RequestAdded());
        await rig.Director.HandleAsync(new RequestAdded());

        rig.Journal.Should().BeEmpty();
    }

    [Fact]
    public async Task PollTicks_WhileARequestWaits_JournalTheRequestOnceAndNotEachTick()
    {
        Rig rig = new();
        await rig.OwnerPlays(rig.Spotify, "spotify:track:mine", 10_000);
        await rig.Request(Item("a"));
        int afterRequest = rig.Journal.Count;
        rig.Journal.Select(e => e.Reason)
            .Should()
            .Equal("owner_music", "waiting_for_track_end", "lined_up");

        await rig.Director.HandleAsync(new RequestAdded());
        await rig.Director.HandleAsync(new RequestAdded());

        rig.Journal.Should().HaveCount(afterRequest);

        await rig.Request(Item("b", owner: "bob"));

        rig.Journal.Should().HaveCount(afterRequest + 1, "a new request is a new fact");
    }

    [Fact]
    public async Task PollTicks_WhilePaused_AreNotJournaledEachTick()
    {
        Rig rig = new();
        await rig.OwnerPlays(rig.Spotify, "spotify:track:mine", 10_000);
        await rig.Director.HandleAsync(new OwnerPause());
        int afterPause = rig.Journal.Count;

        await rig.Director.HandleAsync(new RequestAdded());
        await rig.Director.HandleAsync(new RequestAdded());

        rig.Journal.Should().HaveCount(afterPause);
    }
}

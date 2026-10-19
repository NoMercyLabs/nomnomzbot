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

/// <summary>
/// A request that arrives in Idle before an engine ever reported: the director reads that engine's player
/// once, so it does not start the head over the owner's own music (spec section 4, gap of S-PD-6).
/// </summary>
public sealed partial class PlaybackDirectorTests
{
    private static EngineReport OwnerSong(int progressMs, bool isPlaying = true) =>
        new(EngineKind.Spotify, "spotify:track:owner-song", isPlaying, progressMs, 200_000);

    [Fact]
    public async Task Idle_RequestBeforeAnyReport_WhenTheProbeFindsOwnerMusic_WaitsAndDoesNotStartTheHead()
    {
        Rig rig = new();
        rig.Spotify.ProbeReport = OwnerSong(90_000);

        await rig.Request(Item("a"));

        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        rig.QueueIds.Should().Equal("a");
        rig.Spotify.Calls.Should()
            .Equal(
                [new EngineCall("QueueNext", "a")],
                "lined up, the owner's song is not interrupted"
            );
        rig.YouTube.Calls.Should().BeEmpty();
        rig.Journal.Select(e => e.Reason).Should().Contain("waiting_for_track_end");
        rig.Journal.Last().Reason.Should().Be("lined_up");
    }

    [Fact]
    public async Task Idle_RequestBeforeAnyReport_WhenTheProbeFindsOwnerMusicNearItsEnd_LinesTheHeadUpWithoutAnyCut()
    {
        Rig rig = new();
        rig.Spotify.ProbeReport = OwnerSong(196_000);

        await rig.Request(Item("a"));

        rig.Spotify.Calls.Should().Equal(new EngineCall("QueueNext", "a"));
    }

    [Fact]
    public async Task Idle_RequestBeforeAnyReport_WhenTheProbeFindsNothing_StartsTheHead()
    {
        Rig rig = new();

        await rig.Request(Item("a"));

        rig.Spotify.ProbeCount.Should().Be(1);
        rig.Spotify.Calls.Should().Equal(new EngineCall("Start", "a"));
    }

    [Fact]
    public async Task Idle_RequestBeforeAnyReport_ProbesEachEngineOnlyOnce()
    {
        Rig rig = new();
        rig.Spotify.StartResult = EngineStartResult.Refused("device gone");

        await rig.Request(Item("a"));
        await rig.Request(Item("b"));

        rig.Spotify.ProbeCount.Should().Be(1);
        rig.YouTube.ProbeCount.Should().Be(1);
    }

    [Fact]
    public async Task Idle_RequestAfterAReportFromTheEngine_DoesNotProbeIt()
    {
        Rig rig = new();
        await rig.Spotify.EmitAsync(OwnerSong(5_000, isPlaying: false));

        await rig.Request(Item("a"));

        rig.Spotify.ProbeCount.Should().Be(0);
    }
}

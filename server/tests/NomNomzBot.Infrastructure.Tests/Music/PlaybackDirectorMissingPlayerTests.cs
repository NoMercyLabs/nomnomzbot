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
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.ValueObjects;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// 2026-10-19, qtkitte live: with no Music Player page open, every YouTube request was refused twice and then
/// dropped. A missing player is not a fault of the request: it waits until the page opens.
/// </summary>
public sealed partial class PlaybackDirectorTests
{
    [Fact]
    public async Task NoPlayer_AYouTubeRequestWaitsThroughEveryOwnerTrackAndIsNeverDropped()
    {
        Rig rig = new();
        CloseThePlayer(rig);
        await rig.OwnerPlays(rig.Spotify, "spotify:track:one", 10_000);
        await rig.Request(Item("y1", EngineKind.YouTube));

        foreach ((string ended, string next) in OwnerTracks)
        {
            await rig.Spotify.EmitAsync(new TrackEnded(EngineKind.Spotify, ended));
            await rig.OwnerPlays(rig.Spotify, next, 500);
            await rig.Director.HandleAsync(new RequestAdded());
        }

        rig.QueueIds.Should().Equal("y1");
        rig.Bus.Published.OfType<SongPlaybackFailedEvent>().Should().BeEmpty();
        rig.Spotify.Calls.Should().NotContain(c => c.Name == "Stop" || c.Name == "Pause");
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
    }

    [Fact]
    public async Task NoPlayer_AYouTubeRequestWithNothingPlayingWaitsThroughEveryPollAndIsNeverDropped()
    {
        Rig rig = new();
        CloseThePlayer(rig);
        await rig.Request(Item("y1", EngineKind.YouTube));

        for (int tick = 0; tick < 4; tick++)
            await rig.Director.HandleAsync(new RequestAdded());

        rig.QueueIds.Should().Equal("y1");
        rig.Bus.Published.OfType<SongPlaybackFailedEvent>().Should().BeEmpty();
        rig.Director.State.Kind.Should().Be(PlaybackStateKind.Blocked);
    }

    [Fact]
    public async Task NoPlayer_TheWaitingYouTubeRequestStartsOnceTheMusicPlayerOpens()
    {
        Rig rig = new();
        CloseThePlayer(rig);
        PlaybackItem y1 = Item("y1", EngineKind.YouTube);
        await rig.Request(y1);
        await rig.Director.HandleAsync(new RequestAdded());
        int callsWhileClosed = rig.YouTube.Calls.Count;

        rig.YouTube.BlockCommands = null;
        rig.YouTube.CanStart = true;
        await rig.Director.HandleAsync(new OutputAttached());

        rig.YouTube.Calls.Skip(callsWhileClosed).Should().Equal(new EngineCall("Start", "y1"));
        rig.Director.State.Should().Be(PlaybackState.Starting(EngineKind.YouTube, y1));
        rig.QueueIds.Should().Equal("y1");
    }

    private static readonly (string Ended, string Next)[] OwnerTracks =
    [
        ("spotify:track:one", "spotify:track:two"),
        ("spotify:track:two", "spotify:track:three"),
        ("spotify:track:three", "spotify:track:four"),
    ];

    /// <summary>No Music Player page is open: the engine says it cannot start and blocks every command.</summary>
    private static void CloseThePlayer(Rig rig)
    {
        rig.YouTube.CanStart = false;
        rig.YouTube.BlockCommands = EngineBlockReason.NoOutput;
    }
}

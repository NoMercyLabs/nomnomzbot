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
/// 2026-10-20, Stoney's screenshot: a YouTube request at the head with no Music Player page open sat there while
/// the Spotify requests behind it never advanced. The head waits for the page; the requests behind it play.
/// </summary>
public sealed partial class PlaybackDirectorTests
{
    [Fact]
    public async Task NoPlayer_ASpotifyRequestPlayedBehindTheYouTubeHeadLeavesTheQueueAndTheNextOneLinesUp()
    {
        Rig rig = new();
        PlaybackItem y1 = Item("y1", EngineKind.YouTube, owner: "ifillz");
        PlaybackItem s1 = Item("s1", owner: "alice");
        PlaybackItem s2 = Item("s2", owner: "bob");
        rig.YouTube.CanStart = false;
        rig.YouTube.BlockCommands = EngineBlockReason.NoOutput;
        await rig.OwnerPlays(rig.Spotify, "spotify:track:owner", 10_000);
        await rig.Request(y1);
        await rig.Request(s1);
        await rig.Request(s2);

        await rig.Plays(rig.Spotify, s1);
        await rig.Director.HandleAsync(new RequestAdded());

        rig.QueueIds.Should().Equal("y1", "s2");
        rig.Spotify.Calls.Should().Contain(new EngineCall("QueueNext", "s2"));
        rig.YouTube.Calls.Should().NotContain(c => c.Name == "Start");
        rig.Bus.Published.OfType<SongRequestPassedOverEvent>()
            .Should()
            .ContainSingle(e => e.TrackUri == y1.Uri && e.TrackName == y1.Title);
    }

    [Fact]
    public async Task NoPlayer_NothingPlayingStartsTheSpotifyRequestBehindTheYouTubeHeadAndKeepsTheHead()
    {
        Rig rig = new();
        PlaybackItem y1 = Item("y1", EngineKind.YouTube, owner: "ifillz");
        PlaybackItem s1 = Item("s1", owner: "alice");
        rig.YouTube.CanStart = false;
        rig.YouTube.BlockCommands = EngineBlockReason.NoOutput;
        await rig.Request(y1);
        await rig.Request(s1);

        rig.Director.State.Should().Be(PlaybackState.Starting(EngineKind.Spotify, s1));
        rig.Spotify.Calls.Should().Contain(new EngineCall("Start", "s1"));

        await rig.Plays(rig.Spotify, s1);

        rig.QueueIds.Should().Equal("y1");
        rig.Bus.Published.OfType<SongRequestPassedOverEvent>().Should().ContainSingle();
    }
}

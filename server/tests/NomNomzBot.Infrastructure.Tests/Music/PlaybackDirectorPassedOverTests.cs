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

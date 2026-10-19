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
/// The owner's own music is never cut (live 2026-10-19). A waiting request on the same engine is lined up in
/// that engine's queue before the owner's next track would play; a start that fails while the owner's music
/// plays on follows that music and keeps the request.
/// </summary>
public sealed partial class PlaybackDirectorTests
{
    [Fact]
    public async Task HandOver_OnTheSameEngine_QueuesTheRequestAheadAndNeverCutsTheOwnerTrack()
    {
        Rig rig = new();
        await rig.OwnerPlays(rig.Spotify, "spotify:track:mine", 190_000);
        PlaybackItem a = Item("a");

        await rig.Request(a);
        rig.Clock.Advance(TimeSpan.FromSeconds(5));
        await rig.OwnerPlays(rig.Spotify, "spotify:track:mine", 195_000);
        rig.Clock.Advance(TimeSpan.FromSeconds(5));
        await rig.Ends(rig.Spotify, Item("mine"));
        await rig.Plays(rig.Spotify, a);

        rig.Spotify.Calls.Should().Equal(new EngineCall("QueueNext", "a"));
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, a));
        rig.Journal.Should().NotContain(e => e.Reason == "start_timeout");
    }

    [Fact]
    public async Task PlayingRequestNearItsEnd_LinesTheNextRequestUpInTheQueueWithNoSkip()
    {
        Rig rig = new();
        PlaybackItem a = await rig.StartPlaying(EngineKind.Spotify);
        PlaybackItem b = Item("b", owner: "owner-b");
        await rig.Request(b);

        await rig.Spotify.EmitAsync(
            new EngineReport(EngineKind.Spotify, a.Uri, true, a.DurationMs - 12_000, a.DurationMs)
        );
        await rig.Ends(rig.Spotify, a);
        await rig.Plays(rig.Spotify, b);

        rig.Spotify.Calls.Should()
            .Equal(new EngineCall("Start", "a"), new EngineCall("QueueNext", "b"));
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, b));
    }

    [Fact]
    public async Task FailedStart_OfAnItemSpotifyAlreadyHolds_IsNeverQueuedASecondTime()
    {
        Rig rig = new();
        await rig.Request(Item("a"));
        rig.Clock.Advance(TimeSpan.FromSeconds(11));
        await rig.Director.CheckDeadlinesAsync();
        rig.Director.State.Kind.Should().Be(PlaybackStateKind.Blocked);

        await rig.Director.HandleAsync(new RequestAdded());

        rig.Spotify.Calls.Should().Equal([new EngineCall("Start", "a")], "Spotify still holds it");
    }

    [Fact]
    public async Task LinedUpRequest_WhileTheOwnerTrackPlays_RaisesNoStartTimeoutAndStaysInPlaying()
    {
        Rig rig = new();
        await rig.OwnerPlays(rig.Spotify, "spotify:track:mine", 190_000);
        await rig.Request(Item("a"));

        rig.Clock.Advance(TimeSpan.FromSeconds(20));
        await rig.Director.CheckDeadlinesAsync();

        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        rig.Spotify.Calls.Should().Equal(new EngineCall("QueueNext", "a"));
        rig.Journal.Select(e => e.Reason).Should().NotContain("start_timeout");
        rig.Journal.Last().Reason.Should().Be("lined_up");
    }

    [Fact]
    public async Task FailedStart_WhileOwnerMusicPlaysOn_FollowsTheOwnerAndKeepsTheRequest()
    {
        Rig rig = new();
        await rig.OwnerPlays(rig.Spotify, "spotify:track:mine", 196_000);
        PlaybackItem a = Item("a");
        await rig.Request(a);
        await rig.OwnerPlays(rig.Spotify, "spotify:track:next-in-playlist", 2_000);

        rig.Clock.Advance(TimeSpan.FromSeconds(15));
        await rig.OwnerPlays(rig.Spotify, "spotify:track:next-in-playlist", 17_000);
        await rig.Director.HandleAsync(new RequestAdded());
        await rig.OwnerPlays(rig.Spotify, "spotify:track:next-in-playlist", 22_000);

        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        rig.QueueIds.Should().Equal("a");
        rig.Spotify.Calls.Should()
            .Equal([new EngineCall("QueueNext", "a")], "nothing cuts into the owner's song");
        rig.Bus.Published.OfType<SongPlaybackFailedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task LinedUpRequest_WhileTheOwnersMusicPlaysOn_IsNeverQueuedASecondTime()
    {
        Rig rig = new();
        await rig.OwnerPlays(rig.Spotify, "spotify:track:mine", 196_000);
        await rig.Request(Item("a"));
        await rig.OwnerPlays(rig.Spotify, "spotify:track:next-in-playlist", 2_000);
        rig.Clock.Advance(TimeSpan.FromSeconds(15));
        await rig.OwnerPlays(rig.Spotify, "spotify:track:next-in-playlist", 17_000);

        await rig.OwnerPlays(rig.Spotify, "spotify:track:next-in-playlist", 190_000);

        rig.Spotify.Calls.Should().Equal(new EngineCall("QueueNext", "a"));
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        rig.QueueIds.Should().Equal("a");
    }
}

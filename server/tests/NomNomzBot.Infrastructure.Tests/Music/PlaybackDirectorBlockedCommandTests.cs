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
/// A real engine throws a typed block when it cannot obey a command (no device, no player, dead token).
/// The director turns that into Blocked with the typed reason; it never lets the throw out.
/// </summary>
public sealed partial class PlaybackDirectorTests
{
    private static async Task<Rig> PlayingRequest()
    {
        Rig rig = new();
        await rig.StartPlaying(EngineKind.Spotify);
        return rig;
    }

    [Fact]
    public async Task OwnerPause_EngineBlocked_MovesToBlockedWithTheTypedReason()
    {
        Rig rig = await PlayingRequest();
        rig.Spotify.BlockCommands = EngineBlockReason.NoOutput;

        await rig.Director.HandleAsync(new OwnerPause());

        rig.Director.State.Should().Be(PlaybackState.Blocked("NoOutput"));
        rig.Spotify.Calls.Select(c => c.Name).Should().Equal("Start", "Pause");
        PlaybackTransitionedEventLast(rig).Should().Be(("Playing", "Blocked", "engine_blocked"));
    }

    [Fact]
    public async Task OwnerSeek_EngineBlocked_MovesToBlockedWithTheTypedReason()
    {
        Rig rig = await PlayingRequest();
        rig.Spotify.BlockCommands = EngineBlockReason.Forbidden;

        await rig.Director.HandleAsync(new OwnerSeek(30_000));

        rig.Director.State.Should().Be(PlaybackState.Blocked("Forbidden"));
        rig.Spotify.Calls.Last().Should().Be(new EngineCall("Seek", "30000"));
    }

    [Fact]
    public async Task OwnerVolume_EngineBlocked_MovesToBlockedWithTheTypedReason()
    {
        Rig rig = await PlayingRequest();
        rig.Spotify.BlockCommands = EngineBlockReason.NotConnected;

        await rig.Director.HandleAsync(new OwnerVolume(40));

        rig.Director.State.Should().Be(PlaybackState.Blocked("NotConnected"));
        rig.Spotify.Calls.Last().Should().Be(new EngineCall("SetVolume", "40"));
    }

    [Fact]
    public async Task OwnerResume_EngineBlocked_StaysOutOfPlayingAndIsBlocked()
    {
        Rig rig = await PlayingRequest();
        await rig.Director.HandleAsync(new OwnerPause());
        rig.Spotify.BlockCommands = EngineBlockReason.NoOutput;

        await rig.Director.HandleAsync(new OwnerResume());

        rig.Director.State.Should().Be(PlaybackState.Blocked("NoOutput"));
        rig.Spotify.Calls.Select(c => c.Name).Should().Equal("Start", "Pause", "Resume");
    }

    [Fact]
    public async Task CrossEngineHandOver_StopBlocked_KeepsTheHeadAndNeverStartsTheOtherEngine()
    {
        Rig rig = await PlayingRequest();
        rig.Spotify.BlockCommands = EngineBlockReason.NoOutput;
        PlaybackItem yt = Item("y1", EngineKind.YouTube);
        rig.Queue.Enqueue(yt);

        await rig.Director.HandleAsync(new OwnerSkip());

        rig.Director.State.Should().Be(PlaybackState.Blocked("NoOutput"));
        rig.YouTube.Calls.Should().BeEmpty();
        rig.QueueIds.Should().Equal("y1");
    }

    [Fact]
    public async Task Idle_OutputAttachedWithAWaitingHead_StartsTheHead()
    {
        Rig rig = new();
        PlaybackItem a = Item("a");
        rig.Queue.Enqueue(a);

        await rig.Director.HandleAsync(new OutputAttached());

        rig.Spotify.Calls.Should().Equal(new EngineCall("Start", "a"));
        rig.Director.State.Should().Be(PlaybackState.Starting(EngineKind.Spotify, a));
        PlaybackTransitionedEventLast(rig).Should().Be(("Idle", "Starting", "start_head"));
    }

    [Fact]
    public async Task Idle_OutputAttachedWithAnEmptyQueue_MakesNoEngineCall()
    {
        Rig rig = new();

        await rig.Director.HandleAsync(new OutputAttached());

        rig.Spotify.Calls.Should().BeEmpty();
        rig.YouTube.Calls.Should().BeEmpty();
        rig.Director.State.Should().Be(PlaybackState.Idle);
    }

    private static (string From, string To, string Reason) PlaybackTransitionedEventLast(Rig rig) =>
        rig.Journal.Select(e => (e.From, e.To, e.Reason)).Last();
}

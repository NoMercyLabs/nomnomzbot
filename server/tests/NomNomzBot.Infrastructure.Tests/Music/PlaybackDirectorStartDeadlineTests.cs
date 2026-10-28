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
/// No timer drives the director, so a passed start deadline must be raised by the next input. Live
/// 2026-10-19: a start that Spotify never confirmed held the channel in Starting with no music.
/// </summary>
public sealed partial class PlaybackDirectorTests
{
    [Fact]
    public async Task Starting_WhenTheDeadlinePassed_TheNextPollLeavesStarting()
    {
        Rig rig = new();
        await rig.Request(Item("a"));
        rig.Director.State.Kind.Should().Be(PlaybackStateKind.Starting);

        rig.Clock.Advance(TimeSpan.FromSeconds(11));
        await rig.Spotify.EmitAsync(
            new EngineReport(EngineKind.Spotify, "spotify:track:other", false, 0, 200_000)
        );

        rig.Director.State.Kind.Should().NotBe(PlaybackStateKind.Starting);
        rig.Journal.Should().Contain(e => e.Reason == "start_timeout");
    }

    [Fact]
    public async Task Starting_WhenTheDeadlinePassedButSpotifysOwnQueueStillHoldsTheItem_ItKeepsWaiting()
    {
        Rig rig = new();
        await rig.Request(Item("a"));
        rig.Spotify.Held.Add("a");

        rig.Clock.Advance(TimeSpan.FromSeconds(11));
        await rig.Spotify.EmitAsync(
            new EngineReport(EngineKind.Spotify, "spotify:track:other", false, 0, 200_000)
        );

        rig.Director.State.Kind.Should().Be(PlaybackStateKind.Starting);
        rig.Director.State.Item!.Id.Should().Be("a");
        rig.Journal.Should().NotContain(e => e.Reason == "start_timeout");
        rig.Bus.Published.Should().NotContain(e => e is SongPlaybackFailedEvent);
    }

    [Fact]
    public async Task Starting_WhenTheDeadlinePassedAndSpotifysQueueNoLongerHoldsTheItem_ItFails()
    {
        Rig rig = new();
        await rig.Request(Item("a"));

        rig.Clock.Advance(TimeSpan.FromSeconds(11));
        await rig.Spotify.EmitAsync(
            new EngineReport(EngineKind.Spotify, "spotify:track:other", false, 0, 200_000)
        );

        rig.Journal.Should().Contain(e => e.Reason == "start_timeout");
    }

    [Fact]
    public async Task Starting_BeforeTheDeadline_APollKeepsWaiting()
    {
        Rig rig = new();
        await rig.Request(Item("a"));

        rig.Clock.Advance(TimeSpan.FromSeconds(5));
        await rig.Spotify.EmitAsync(
            new EngineReport(EngineKind.Spotify, "spotify:track:other", false, 0, 200_000)
        );

        rig.Director.State.Kind.Should().Be(PlaybackStateKind.Starting);
        rig.Journal.Should().NotContain(e => e.Reason == "start_timeout");
    }
}

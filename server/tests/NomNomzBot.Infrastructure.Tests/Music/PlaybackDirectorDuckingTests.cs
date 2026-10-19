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

/// <summary>Music mute and ducking apart from bot audio (S-PD-8, rows 25 and 26).</summary>
public sealed partial class PlaybackDirectorTests
{
    [Theory]
    [InlineData(EngineKind.Spotify)]
    [InlineData(EngineKind.YouTube)]
    public async Task Row25_MuteWhileDucked_EndsAtVolumeZeroAndStaysPlaying(EngineKind kind)
    {
        Rig rig = new();
        PlaybackItem a = await rig.StartPlaying(kind);
        await rig.Director.HandleAsync(new OwnerVolume(80));

        await rig.Director.HandleAsync(new DuckStart(50));
        await rig.Director.HandleAsync(new OwnerMute(true));

        rig.Director.State.Should().Be(PlaybackState.Playing(kind, a));
        rig.Director.Level.Should().Be(new PlaybackLevel(80, true));
        rig.Engine(kind)
            .Calls.Skip(1)
            .Should()
            .Equal(
                new EngineCall("SetVolume", "80"),
                new EngineCall("SetVolume", "40"),
                new EngineCall("SetVolume", "0")
            );
        rig.Other(kind).Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(EngineKind.Spotify)]
    [InlineData(EngineKind.YouTube)]
    public async Task Row26_OwnerVolumeDuringADuck_EndsAtTheNewVolumeAfterDuckEnd(EngineKind kind)
    {
        Rig rig = new();
        PlaybackItem a = await rig.StartPlaying(kind);
        await rig.Director.HandleAsync(new OwnerVolume(70));

        await rig.Director.HandleAsync(new DuckStart(30));
        await rig.Director.HandleAsync(new OwnerVolume(40));
        await rig.Director.HandleAsync(new DuckEnd());

        rig.Director.State.Should().Be(PlaybackState.Playing(kind, a));
        rig.Director.Level.Should().Be(new PlaybackLevel(40, false));
        rig.Engine(kind)
            .Calls.Skip(1)
            .Should()
            .Equal(
                new EngineCall("SetVolume", "70"),
                new EngineCall("SetVolume", "21"),
                new EngineCall("SetVolume", "12"),
                new EngineCall("SetVolume", "40")
            );
    }

    [Fact]
    public async Task DuckWhilePaused_StaysPausedAndDuckEndRestoresTheOwnerLevel()
    {
        Rig rig = new();
        PlaybackItem a = await rig.StartPlaying(EngineKind.YouTube);
        await rig.Director.HandleAsync(new OwnerVolume(60));
        await rig.Director.HandleAsync(new OwnerPause());

        await rig.Director.HandleAsync(new DuckStart(50));
        await rig.Director.HandleAsync(new DuckEnd());

        rig.Director.State.Should().Be(PlaybackState.PausedByOwner(EngineKind.YouTube, a));
        rig.Engine(EngineKind.YouTube)
            .Calls.TakeLast(2)
            .Should()
            .Equal(new EngineCall("SetVolume", "30"), new EngineCall("SetVolume", "60"));
    }
}

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
    public async Task AfterARestart_TheLevelIsSpotifysOwnVolume_NotFullVolume()
    {
        // A restart forgets the owner's level. The next duck or crossfade must work from the volume Spotify
        // reports, or the music jumps to 100% on the next song change.
        Rig rig = new();
        PlaybackItem a = await rig.StartPlaying(EngineKind.Spotify);

        await rig.Director.HandleAsync(
            new EngineReport(
                EngineKind.Spotify,
                a.Uri,
                true,
                1_000,
                a.DurationMs,
                VolumePercent: 35
            )
        );
        await rig.Director.HandleAsync(new DuckStart(50));

        rig.Director.Level.Should().Be(new PlaybackLevel(35, false));
        rig.Engine(EngineKind.Spotify).Calls.Last().Should().Be(new EngineCall("SetVolume", "17"));
    }

    [Fact]
    public async Task AReportedVolume_NeverOverridesTheOwnersOwnChoice()
    {
        Rig rig = new();
        PlaybackItem a = await rig.StartPlaying(EngineKind.Spotify);
        await rig.Director.HandleAsync(new OwnerVolume(60));

        await rig.Director.HandleAsync(
            new EngineReport(
                EngineKind.Spotify,
                a.Uri,
                true,
                1_000,
                a.DurationMs,
                VolumePercent: 20
            )
        );

        rig.Director.Level.Should().Be(new PlaybackLevel(60, false));
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

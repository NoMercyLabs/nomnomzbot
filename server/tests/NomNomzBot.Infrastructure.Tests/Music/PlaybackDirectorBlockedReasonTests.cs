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

public sealed partial class PlaybackDirectorTests
{
    [Fact]
    public async Task BlockedReason_NoSpotifyDeviceNamesTheSpotifyDeviceAndClearsWhenADeviceStartsTheHead()
    {
        Rig rig = new();
        rig.Spotify.BlockCommands = EngineBlockReason.NoOutput;
        await rig.Request(Item("a"));

        rig.Director.BlockedReason.Should().Be(PlaybackBlockedReason.NoSpotifyDevice);

        rig.Spotify.BlockCommands = null;
        await rig.Spotify.EmitAsync(
            new EngineReport(EngineKind.Spotify, "spotify:track:old", false, 0, 1_000, "dev-1")
        );

        rig.Director.State.Kind.Should().Be(PlaybackStateKind.Starting);
        rig.Director.BlockedReason.Should().BeNull();
    }

    [Fact]
    public async Task BlockedReason_AYouTubeHeadWithNoPlayerPageNamesTheMusicPlayer()
    {
        Rig rig = new();
        rig.YouTube.CanStart = false;
        rig.YouTube.BlockCommands = EngineBlockReason.NoOutput;
        await rig.Request(Item("y1", EngineKind.YouTube));

        rig.Director.State.Kind.Should().Be(PlaybackStateKind.Blocked);
        rig.Director.BlockedReason.Should().Be(PlaybackBlockedReason.NoMusicPlayer);
    }

    [Fact]
    public async Task BlockedReason_ASpotifyAccountWithoutPremiumNamesPremium()
    {
        Rig rig = new();
        rig.Spotify.BlockCommands = EngineBlockReason.PremiumRequired;
        await rig.Request(Item("a"));

        rig.Director.BlockedReason.Should().Be(PlaybackBlockedReason.PremiumRequired);
    }

    [Fact]
    public async Task BlockedReason_IsNullWhileARequestPlays()
    {
        Rig rig = new();
        PlaybackItem head = Item("a");
        await rig.Request(head);
        await rig.Plays(rig.Spotify, head);

        rig.Director.State.Kind.Should().Be(PlaybackStateKind.Playing);
        rig.Director.BlockedReason.Should().BeNull();
    }
}

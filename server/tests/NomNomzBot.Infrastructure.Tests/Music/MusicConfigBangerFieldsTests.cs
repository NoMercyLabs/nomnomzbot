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
using NomNomzBot.Application.Music.Dtos;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>The three <c>!banger</c> settings travel through PUT and GET of the music config.</summary>
public sealed class MusicConfigBangerFieldsTests
{
    private const string Broadcaster = "0192a000-0000-7000-8000-000000009911";

    [Fact]
    public async Task A_fresh_channel_has_no_playlist_and_auto_create_off()
    {
        using MusicConfigDbFixture fixture = new();

        MusicConfigDto config = (await fixture.Service.GetConfigAsync(Broadcaster)).Value;

        config.BangerPlaylistId.Should().BeNull();
        config.BangerPlaylistProvider.Should().BeNull();
        config.BangerAutoCreate.Should().BeFalse();
    }

    [Fact]
    public async Task The_banger_settings_are_saved_and_read_back_while_the_other_settings_stay()
    {
        using MusicConfigDbFixture fixture = new();
        await fixture.Service.UpdateConfigAsync(
            Broadcaster,
            new UpdateMusicConfigDto { MaxQueueSize = 7 }
        );

        await fixture.Service.UpdateConfigAsync(
            Broadcaster,
            new UpdateMusicConfigDto
            {
                BangerPlaylistId = "37i9dQZF1DXcBWIGoYBM5M",
                BangerPlaylistProvider = "spotify",
                BangerAutoCreate = true,
            }
        );

        MusicConfigDto config = (await fixture.Service.GetConfigAsync(Broadcaster)).Value;
        config.BangerPlaylistId.Should().Be("37i9dQZF1DXcBWIGoYBM5M");
        config.BangerPlaylistProvider.Should().Be("spotify");
        config.BangerAutoCreate.Should().BeTrue();
        config.MaxQueueSize.Should().Be(7);
    }

    [Fact]
    public async Task An_empty_playlist_id_clears_the_choice()
    {
        using MusicConfigDbFixture fixture = new();
        await fixture.Service.UpdateConfigAsync(
            Broadcaster,
            new UpdateMusicConfigDto
            {
                BangerPlaylistId = "pl1",
                BangerPlaylistProvider = "youtube",
            }
        );

        await fixture.Service.UpdateConfigAsync(
            Broadcaster,
            new UpdateMusicConfigDto { BangerPlaylistId = "", BangerPlaylistProvider = "" }
        );

        MusicConfigDto config = (await fixture.Service.GetConfigAsync(Broadcaster)).Value;
        config.BangerPlaylistId.Should().BeNull();
        config.BangerPlaylistProvider.Should().BeNull();
    }
}

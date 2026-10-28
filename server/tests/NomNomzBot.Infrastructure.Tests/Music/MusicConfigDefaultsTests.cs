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

using System.Reflection;
using FluentAssertions;
using NomNomzBot.Application.Music.Dtos;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Every music setting has one default (<see cref="MusicConfigDefaults"/>). A channel with no saved row
/// reads those defaults from the API, and every settable field is saved and read back.
/// </summary>
public sealed class MusicConfigDefaultsTests
{
    private const string Broadcaster = "0192a000-0000-7000-8000-000000009922";

    [Fact]
    public async Task A_channel_with_no_saved_row_reads_every_default_from_the_api()
    {
        using MusicConfigDbFixture fixture = new();

        MusicConfigDto config = (await fixture.Service.GetConfigAsync(Broadcaster)).Value;

        config.IsEnabled.Should().Be(MusicConfigDefaults.IsEnabled);
        config.RequestsOpen.Should().Be(MusicConfigDefaults.RequestsOpen);
        config.PreferredProvider.Should().Be(MusicConfigDefaults.PreferredProvider);
        config.MaxQueueSize.Should().Be(MusicConfigDefaults.MaxQueueSize);
        config.MaxRequestsPerUser.Should().Be(MusicConfigDefaults.MaxRequestsPerUser);
        config.MaxSongSeconds.Should().Be(MusicConfigDefaults.MaxSongSeconds);
        config.AllowExplicit.Should().Be(MusicConfigDefaults.AllowExplicit);
        config.AllowYouTube.Should().Be(MusicConfigDefaults.AllowYouTube);
        config.AllowSpotify.Should().Be(MusicConfigDefaults.AllowSpotify);
        config.MinTrustLevel.Should().Be(MusicConfigDefaults.MinTrustLevel);
        config.BangerAutoCreate.Should().Be(MusicConfigDefaults.BangerAutoCreate);
        config.PerStreamLimit.Should().BeNull();
        config.BangerPlaylistId.Should().BeNull();
        config.BangerPlaylistProvider.Should().BeNull();
        config.MaxRequestsPerRole.Should().BeNull();
    }

    [Fact]
    public async Task GetDefaults_returns_what_a_channel_with_no_row_resolves_to()
    {
        using MusicConfigDbFixture fixture = new();

        MusicConfigDto defaults = fixture.Service.GetDefaults();
        MusicConfigDto neverSet = (await fixture.Service.GetConfigAsync(Broadcaster)).Value;

        defaults.Should().BeEquivalentTo(neverSet);
        defaults.RequestsOpen.Should().Be(MusicConfigDefaults.RequestsOpen);
    }

    [Fact]
    public void The_queue_dto_carries_no_RequestsOpen_default_of_its_own()
    {
        ParameterInfo parameter = typeof(MusicQueueDto)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Single(p => p.Name == nameof(MusicQueueDto.RequestsOpen));

        parameter.HasDefaultValue.Should().BeFalse();
    }

    [Fact]
    public void A_read_dto_built_without_the_optional_fields_carries_the_same_defaults()
    {
        MusicConfigDto config = new(true, "auto", 50, 5, false, true, "everyone");

        config.RequestsOpen.Should().Be(MusicConfigDefaults.RequestsOpen);
        config.MaxSongSeconds.Should().Be(MusicConfigDefaults.MaxSongSeconds);
        config.AllowExplicit.Should().Be(MusicConfigDefaults.AllowExplicit);
        config.BangerAutoCreate.Should().Be(MusicConfigDefaults.BangerAutoCreate);
    }

    [Fact]
    public async Task Every_settable_field_is_saved_and_read_back_changed()
    {
        using MusicConfigDbFixture fixture = new();

        await fixture.Service.UpdateConfigAsync(
            Broadcaster,
            new UpdateMusicConfigDto
            {
                IsEnabled = !MusicConfigDefaults.IsEnabled,
                RequestsOpen = !MusicConfigDefaults.RequestsOpen,
                PreferredProvider = "youtube",
                MaxQueueSize = 11,
                MaxRequestsPerUser = 3,
                MaxSongSeconds = 90,
                AllowExplicit = !MusicConfigDefaults.AllowExplicit,
                PerStreamLimit = 4,
                AllowYouTube = !MusicConfigDefaults.AllowYouTube,
                AllowSpotify = !MusicConfigDefaults.AllowSpotify,
                MinTrustLevel = "moderators",
                BangerPlaylistId = "pl-1",
                BangerPlaylistProvider = "spotify",
                BangerAutoCreate = !MusicConfigDefaults.BangerAutoCreate,
                MaxRequestsPerRole = new() { ["vip"] = 8 },
            }
        );

        MusicConfigDto config = (await fixture.Service.GetConfigAsync(Broadcaster)).Value;
        config.IsEnabled.Should().Be(!MusicConfigDefaults.IsEnabled);
        config.RequestsOpen.Should().Be(!MusicConfigDefaults.RequestsOpen);
        config.PreferredProvider.Should().Be("youtube");
        config.MaxQueueSize.Should().Be(11);
        config.MaxRequestsPerUser.Should().Be(3);
        config.MaxSongSeconds.Should().Be(90);
        config.AllowExplicit.Should().Be(!MusicConfigDefaults.AllowExplicit);
        config.PerStreamLimit.Should().Be(4);
        config.AllowYouTube.Should().Be(!MusicConfigDefaults.AllowYouTube);
        config.AllowSpotify.Should().Be(!MusicConfigDefaults.AllowSpotify);
        config.MinTrustLevel.Should().Be("moderators");
        config.BangerPlaylistId.Should().Be("pl-1");
        config.BangerPlaylistProvider.Should().Be("spotify");
        config.BangerAutoCreate.Should().Be(!MusicConfigDefaults.BangerAutoCreate);
        config
            .MaxRequestsPerRole.Should()
            .BeEquivalentTo(new Dictionary<string, int> { ["vip"] = 8 });
    }
}

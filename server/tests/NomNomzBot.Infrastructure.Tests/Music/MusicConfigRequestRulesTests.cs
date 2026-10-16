// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using NomNomzBot.Application.Music.Dtos;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>The longest-song, explicit and per-stream dials travel through PUT and GET of the music config.</summary>
public sealed class MusicConfigRequestRulesTests
{
    private const string Broadcaster = "0192a000-0000-7000-8000-000000009913";

    [Fact]
    public async Task A_fresh_channel_gets_the_spec_defaults()
    {
        using MusicConfigDbFixture fixture = new();

        MusicConfigDto config = (await fixture.Service.GetConfigAsync(Broadcaster)).Value;

        config.MaxSongSeconds.Should().Be(360);
        config.AllowExplicit.Should().BeTrue();
        config.PerStreamLimit.Should().BeNull();
    }

    [Fact]
    public async Task The_dials_are_saved_read_back_and_a_later_save_of_other_fields_keeps_them()
    {
        using MusicConfigDbFixture fixture = new();
        await fixture.Service.UpdateConfigAsync(
            Broadcaster,
            new UpdateMusicConfigDto
            {
                MaxSongSeconds = 240,
                AllowExplicit = false,
                PerStreamLimit = 3,
            }
        );

        await fixture.Service.UpdateConfigAsync(
            Broadcaster,
            new UpdateMusicConfigDto { MaxQueueSize = 9 }
        );

        MusicConfigDto config = (await fixture.Service.GetConfigAsync(Broadcaster)).Value;
        config.MaxSongSeconds.Should().Be(240);
        config.AllowExplicit.Should().BeFalse();
        config.PerStreamLimit.Should().Be(3);
        config.MaxQueueSize.Should().Be(9);
    }

    [Fact]
    public async Task A_per_stream_limit_of_zero_turns_the_cap_off()
    {
        using MusicConfigDbFixture fixture = new();
        await fixture.Service.UpdateConfigAsync(
            Broadcaster,
            new UpdateMusicConfigDto { PerStreamLimit = 3 }
        );

        await fixture.Service.UpdateConfigAsync(
            Broadcaster,
            new UpdateMusicConfigDto { PerStreamLimit = 0 }
        );

        (await fixture.Service.GetConfigAsync(Broadcaster)).Value.PerStreamLimit.Should().BeNull();
    }

    [Theory]
    [InlineData(nameof(UpdateMusicConfigDto.MaxSongSeconds), 29)]
    [InlineData(nameof(UpdateMusicConfigDto.MaxSongSeconds), 3601)]
    [InlineData(nameof(UpdateMusicConfigDto.PerStreamLimit), 101)]
    [InlineData(nameof(UpdateMusicConfigDto.PerStreamLimit), -1)]
    public void A_value_outside_its_range_fails_validation(string field, int value)
    {
        UpdateMusicConfigDto dto = field switch
        {
            nameof(UpdateMusicConfigDto.MaxSongSeconds) => new() { MaxSongSeconds = value },
            _ => new() { PerStreamLimit = value },
        };

        List<ValidationResult> errors = [];
        bool valid = Validator.TryValidateObject(dto, new(dto), errors, true);

        valid.Should().BeFalse();
        errors.SelectMany(e => e.MemberNames).Should().Contain(field);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(3600)]
    public void The_song_length_edges_pass_validation(int seconds)
    {
        UpdateMusicConfigDto dto = new() { MaxSongSeconds = seconds };

        Validator.TryValidateObject(dto, new(dto), [], true).Should().BeTrue();
    }
}

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

/// <summary>The per-role song request cap travels through PUT and GET of the music config.</summary>
public sealed class MusicConfigRoleLimitTests
{
    private const string Broadcaster = "0192a000-0000-7000-8000-000000009912";

    [Fact]
    public async Task A_fresh_channel_has_no_role_caps()
    {
        using MusicConfigDbFixture fixture = new();

        MusicConfigDto config = (await fixture.Service.GetConfigAsync(Broadcaster)).Value;

        (config.MaxRequestsPerRole ?? new Dictionary<string, int>()).Should().BeEmpty();
    }

    [Fact]
    public async Task The_role_caps_are_saved_and_read_back_while_the_other_settings_stay()
    {
        using MusicConfigDbFixture fixture = new();
        await fixture.Service.UpdateConfigAsync(
            Broadcaster,
            new UpdateMusicConfigDto { MaxQueueSize = 7, MaxRequestsPerUser = 2 }
        );

        await fixture.Service.UpdateConfigAsync(
            Broadcaster,
            new UpdateMusicConfigDto
            {
                MaxRequestsPerRole = new() { ["moderator"] = 3, ["subscriber"] = 4 },
            }
        );

        MusicConfigDto config = (await fixture.Service.GetConfigAsync(Broadcaster)).Value;
        config.MaxRequestsPerRole.Should().NotBeNull();
        config
            .MaxRequestsPerRole!.Should()
            .BeEquivalentTo(new Dictionary<string, int> { ["moderator"] = 3, ["subscriber"] = 4 });
        config.MaxQueueSize.Should().Be(7);
        config.MaxRequestsPerUser.Should().Be(2);
    }

    [Theory]
    [InlineData("moderator", 0)]
    [InlineData("moderator", 51)]
    [InlineData("follower", 3)]
    [InlineData("Moderator", 3)]
    public void A_cap_outside_one_to_fifty_or_an_unknown_role_key_fails_validation(
        string role,
        int cap
    )
    {
        UpdateMusicConfigDto dto = new() { MaxRequestsPerRole = new() { [role] = cap } };

        List<ValidationResult> errors = [];
        bool valid = Validator.TryValidateObject(dto, new(dto), errors, true);

        valid.Should().BeFalse();
        errors
            .SelectMany(e => e.MemberNames)
            .Should()
            .Contain(nameof(UpdateMusicConfigDto.MaxRequestsPerRole));
    }

    [Fact]
    public void Every_known_role_key_with_a_cap_in_range_passes_validation()
    {
        UpdateMusicConfigDto dto = new()
        {
            MaxRequestsPerRole = new()
            {
                ["viewer"] = 1,
                ["subscriber"] = 2,
                ["vip"] = 3,
                ["moderator"] = 4,
                ["broadcaster"] = 50,
            },
        };

        Validator.TryValidateObject(dto, new(dto), [], true).Should().BeTrue();
    }
}

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
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Api.Tests.Hubs;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Infrastructure.Sound;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>The audio-mix endpoint reads and writes the channel's master and TTS volume on the bot.</summary>
public sealed class AudioMixControllerTests
{
    private static readonly Guid Channel = Guid.Parse("019f2d00-1111-7000-8000-000000000001");

    private static AudioMixController Build(WidgetTestDbContext db)
    {
        ICurrentTenantService tenant = Substitute.For<ICurrentTenantService>();
        tenant.BroadcasterId.Returns(Channel);
        return new(new ChannelAudioMixService(db), tenant);
    }

    private static ChannelAudioMixDto Body(IActionResult result) =>
        result
            .Should()
            .BeOfType<OkObjectResult>()
            .Which.Value.Should()
            .BeOfType<StatusResponseDto<ChannelAudioMixDto>>()
            .Which.Data!;

    [Fact]
    public async Task A_new_channel_reads_100_and_100()
    {
        await using WidgetTestDbContext db = WidgetTestDbContext.New();

        ChannelAudioMixDto mix = Body(await Build(db).Get(CancellationToken.None));

        mix.Should().Be(new ChannelAudioMixDto(100, 100));
    }

    [Fact]
    public async Task An_out_of_range_put_is_a_400_with_the_localized_key_and_stores_nothing()
    {
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        AudioMixController controller = Build(db);

        IActionResult result = await controller.Update(new(101, 50), CancellationToken.None);

        result
            .Should()
            .BeOfType<BadRequestObjectResult>()
            .Which.Value.Should()
            .BeOfType<StatusResponseDto<object>>()
            .Which.Message.Should()
            .Be("audio_mix_master_volume_out_of_range");
        ChannelAudioMixDto read = Body(await controller.Get(CancellationToken.None));
        read.Should().Be(new ChannelAudioMixDto(100, 100));
    }

    [Fact]
    public async Task A_put_is_stored_and_the_next_get_returns_it()
    {
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        AudioMixController controller = Build(db);

        ChannelAudioMixDto put = Body(await controller.Update(new(55, 35), CancellationToken.None));
        ChannelAudioMixDto read = Body(await controller.Get(CancellationToken.None));

        put.Should().Be(new ChannelAudioMixDto(55, 35));
        read.MusicDuckPercent.Should().Be(100);
        read.Should().Be(new ChannelAudioMixDto(55, 35));
    }

    [Fact]
    public async Task The_music_duck_percent_goes_through_the_put_and_the_next_get_returns_it()
    {
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        AudioMixController controller = Build(db);

        ChannelAudioMixDto put = Body(
            await controller.Update(new(55, 35, MusicDuckPercent: 30), CancellationToken.None)
        );
        ChannelAudioMixDto read = Body(await controller.Get(CancellationToken.None));

        put.Should().Be(new ChannelAudioMixDto(55, 35, 30));
        read.Should().Be(new ChannelAudioMixDto(55, 35, 30));
        read.DefaultMusicDuckPercent.Should().Be(100);
    }
}

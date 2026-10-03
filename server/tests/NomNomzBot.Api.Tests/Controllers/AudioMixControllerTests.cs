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
    public async Task A_put_is_stored_and_the_next_get_returns_it()
    {
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        AudioMixController controller = Build(db);

        ChannelAudioMixDto put = Body(await controller.Update(new(55, 35), CancellationToken.None));
        ChannelAudioMixDto read = Body(await controller.Get(CancellationToken.None));

        put.Should().Be(new ChannelAudioMixDto(55, 35));
        read.Should().Be(new ChannelAudioMixDto(55, 35));
    }
}

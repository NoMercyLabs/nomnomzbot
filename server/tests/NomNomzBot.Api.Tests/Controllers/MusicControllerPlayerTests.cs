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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The dashboard player's previous and volume keys (Stoney 2026-10-14: "a stable music player interface with
/// the correct buttons, a proper volume slider"). Each reaches the provider through the music service, and a
/// refusal from the provider comes back as the refusal, not as a success.
/// </summary>
public sealed class MusicControllerPlayerTests
{
    private const string ChannelId = "11111111-1111-1111-1111-111111111111";

    private static MusicController Build(IMusicService music) =>
        new(
            music,
            Substitute.For<IMusicConfigService>(),
            Substitute.For<ISongRequestPageTokenService>(),
            Substitute.For<IBlockedTrackService>(),
            Substitute.For<IMusicHistoryReader>()
        )
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() },
        };

    [Fact]
    public async Task Previous_goes_back_on_the_provider_for_that_channel()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music.PreviousAsync(ChannelId, Arg.Any<CancellationToken>()).Returns(Result.Success());

        IActionResult result = await Build(music).Previous(ChannelId, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        await music.Received(1).PreviousAsync(ChannelId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetVolume_sends_the_percent_to_the_provider()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music.SetVolumeAsync(ChannelId, 42, Arg.Any<CancellationToken>()).Returns(Result.Success());

        IActionResult result = await Build(music)
            .SetVolume(ChannelId, new VolumeDto(42), CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        await music.Received(1).SetVolumeAsync(ChannelId, 42, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetVolume_returns_the_provider_refusal_instead_of_success()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .SetVolumeAsync(ChannelId, 42, Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Spotify Premium is required.", "PREMIUM_REQUIRED"));

        IActionResult result = await Build(music)
            .SetVolume(ChannelId, new VolumeDto(42), CancellationToken.None);

        result.Should().NotBeOfType<NoContentResult>();
    }
}

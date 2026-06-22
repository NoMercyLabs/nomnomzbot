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
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the public (JWT-less) song-request controller: a viewer submission resolves the page token, refuses when
/// the channel is closed (409) or the token is unknown (404), and otherwise queues the requested track against the
/// resolved broadcaster.
/// </summary>
public sealed class PublicSongRequestControllerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f0c1");

    private static (
        PublicSongRequestController Controller,
        ISongRequestPageTokenService Tokens,
        IMusicService Music
    ) Build()
    {
        ISongRequestPageTokenService tokens = Substitute.For<ISongRequestPageTokenService>();
        IMusicService music = Substitute.For<IMusicService>();
        return (new PublicSongRequestController(tokens, music), tokens, music);
    }

    private static SongRequestPageDto Page(bool accepting) =>
        new(Channel, "CoolStreamer", accepting, ["spotify"]);

    [Fact]
    public async Task Submit_with_unknown_token_returns_not_found()
    {
        (PublicSongRequestController controller, ISongRequestPageTokenService tokens, _) = Build();
        tokens
            .ResolveAsync("nope", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<SongRequestPageDto>("Unknown song-request page.", "NOT_FOUND"));

        IActionResult result = await controller.Submit(
            "nope",
            new SongRequestDto { Query = "a song" },
            default
        );

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Submit_when_channel_closed_returns_conflict()
    {
        (PublicSongRequestController controller, ISongRequestPageTokenService tokens, _) = Build();
        tokens
            .ResolveAsync("tok", Arg.Any<CancellationToken>())
            .Returns(Result.Success(Page(accepting: false)));

        IActionResult result = await controller.Submit(
            "tok",
            new SongRequestDto { Query = "a song" },
            default
        );

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Submit_queues_the_track_against_the_resolved_broadcaster()
    {
        (
            PublicSongRequestController controller,
            ISongRequestPageTokenService tokens,
            IMusicService music
        ) = Build();
        tokens
            .ResolveAsync("tok", Arg.Any<CancellationToken>())
            .Returns(Result.Success(Page(accepting: true)));
        music
            .AddToQueueAsync(
                Channel.ToString(),
                "never gonna give you up",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);

        IActionResult result = await controller.Submit(
            "tok",
            new SongRequestDto { Query = "never gonna give you up" },
            default
        );

        result.Should().BeOfType<OkObjectResult>();
        await music
            .Received()
            .AddToQueueAsync(
                Channel.ToString(),
                "never gonna give you up",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }
}

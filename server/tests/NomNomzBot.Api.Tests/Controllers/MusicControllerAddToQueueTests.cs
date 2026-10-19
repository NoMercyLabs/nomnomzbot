// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Interfaces;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves <see cref="MusicController.AddToQueue"/> attributes a song request to the real, identifiable
/// requester rather than an anonymous default. Before this fix, an authenticated caller who did not
/// supply <see cref="SongRequestDto.RequestedBy"/> (the participant dashboard's own song-request flow
/// always sends null) had the request routed to <c>MusicService</c> with a null requester, which the
/// admission path defaults to "anonymous" — losing the real viewer's identity even though it was sitting
/// right there in the JWT. An operator explicitly naming a target viewer must still be honored as-is.
/// </summary>
public sealed class MusicControllerAddToQueueTests
{
    private const string ChannelId = "11111111-1111-1111-1111-111111111111";

    private static IRoleResolver ResolverReturning(int level)
    {
        IRoleResolver roles = Substitute.For<IRoleResolver>();
        roles
            .ResolveEffectiveLevelAsync(
                Arg.Any<Guid>(),
                Guid.Parse(ChannelId),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(level));
        return roles;
    }

    private static MusicController Build(IMusicService music, string? callerDisplayName)
    {
        List<Claim> claims = [new(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString())];
        if (callerDisplayName is not null)
            claims.Add(new("display_name", callerDisplayName));

        return new MusicController(
            music,
            Substitute.For<IMusicConfigService>(),
            Substitute.For<ISongRequestPageTokenService>(),
            Substitute.For<IBlockedTrackService>(),
            Substitute.For<IMusicHistoryReader>(),
            Substitute.For<ISongRequestIntakeService>(),
            Substitute.For<IPlaybackStateReader>()
        )
        {
            ControllerContext = new()
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new(new ClaimsIdentity(claims, "test")),
                },
            },
        };
    }

    [Fact]
    public async Task AddToQueue_without_an_explicit_requester_attributes_to_the_caller_not_anonymous()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .RequestTrackAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>()
            )
            .Returns(
                Result.Success(
                    new MusicTrack("uri:1", "Track", "Artist", null, null, 1000, "spotify")
                )
            );

        MusicController controller = Build(music, callerDisplayName: "RealViewer42");

        IActionResult result = await controller.AddToQueue(
            ChannelId,
            new SongRequestDto { Query = "some song", RequestedBy = null },
            ResolverReturning(0),
            CancellationToken.None
        );

        result.Should().BeOfType<OkObjectResult>();
        await music
            .Received(1)
            .RequestTrackAsync(
                ChannelId,
                "some song",
                "RealViewer42",
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>()
            );
    }

    [Fact]
    public async Task AddToQueue_with_an_explicit_target_requester_honors_it_over_the_caller()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .RequestTrackAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>()
            )
            .Returns(
                Result.Success(
                    new MusicTrack("uri:1", "Track", "Artist", null, null, 1000, "spotify")
                )
            );

        // The operator dashboard, queuing on behalf of a specific viewer.
        MusicController controller = Build(music, callerDisplayName: "OperatorMod");

        IActionResult result = await controller.AddToQueue(
            ChannelId,
            new SongRequestDto { Query = "some song", RequestedBy = "TargetViewer" },
            ResolverReturning(0),
            CancellationToken.None
        );

        result.Should().BeOfType<OkObjectResult>();
        await music
            .Received(1)
            .RequestTrackAsync(
                ChannelId,
                "some song",
                "TargetViewer",
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>()
            );
    }

    [Fact]
    public async Task AddToQueue_passes_the_callers_resolved_role_level_so_each_service_gate_can_judge_it()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .RequestTrackAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>()
            )
            .Returns(
                Result.Success(
                    new MusicTrack("uri:1", "Track", "Artist", null, null, 1000, "youtube")
                )
            );
        IRoleResolver roles = ResolverReturning(40);
        MusicController controller = Build(music, callerDisplayName: "Streamer");

        await controller.AddToQueue(
            ChannelId,
            new SongRequestDto { Query = "some song" },
            roles,
            CancellationToken.None
        );

        await music
            .Received(1)
            .RequestTrackAsync(
                ChannelId,
                "some song",
                "Streamer",
                40,
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>()
            );
    }
}

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
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// <c>GET /channels?deleted=true</c> lists the caller's soft-deleted channels, not their live ones: the rows keep
/// the delete date and the restore deadline, and the live-list work (Twitch moderated lookup, membership grants)
/// never runs.
/// </summary>
public sealed class ChannelsControllerDeletedListTests
{
    private static readonly Guid Caller = Guid.Parse("0192a000-0000-7000-8000-000000000e01");
    private static readonly DateTime DeletedAt = new(2026, 10, 10, 8, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ListChannels_with_deleted_returns_the_deleted_rows_with_both_dates()
    {
        IChannelService channels = Substitute.For<IChannelService>();
        ChannelSummaryDto row = new(
            "01JABCDEF",
            "streamer",
            "Streamer",
            null,
            false,
            "broadcaster",
            null,
            "twitch",
            DeletedAt: DeletedAt,
            PermanentAfter: DeletedAt.AddDays(30)
        );
        channels
            .GetDeletedChannelsAsync(
                Caller.ToString(),
                Arg.Any<PaginationParams>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new PagedList<ChannelSummaryDto>([row], 1, 25, 1)));
        IChannelAccessService access = Substitute.For<IChannelAccessService>();
        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();
        ChannelsController controller = Build(channels, access, moderators);

        IActionResult result = await controller.ListChannels(new(), default, deleted: true);

        PaginatedResponse<ChannelSummaryDto> body = result
            .Should()
            .BeOfType<OkObjectResult>()
            .Subject.Value.Should()
            .BeOfType<PaginatedResponse<ChannelSummaryDto>>()
            .Subject;
        ChannelSummaryDto listed = body.Data.Should().ContainSingle().Subject;
        listed.Login.Should().Be("streamer");
        listed.DeletedAt.Should().Be(DeletedAt);
        listed.PermanentAfter.Should().Be(DeletedAt.AddDays(30));
        await channels.DidNotReceiveWithAnyArgs().GetChannelsAsync(default!, default!);
        await access.DidNotReceiveWithAnyArgs().ResolveOwnChannelAsync(default!);
        await moderators.DidNotReceiveWithAnyArgs().GetModeratedChannelsAsync(default, default!);
    }

    private static ChannelsController Build(
        IChannelService channels,
        IChannelAccessService access,
        ITwitchModeratorsApi moderators
    ) =>
        new(
            channels,
            ApiTestDbContext.New(),
            moderators,
            access,
            Substitute.For<IMembershipService>(),
            Substitute.For<IUserService>(),
            Substitute.For<IChannelDeletePreviewService>(),
            Substitute.For<ICurrentUserService>(),
            Substitute.For<IViewAsScope>()
        )
        {
            ControllerContext = new()
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new(
                        new ClaimsIdentity(
                            [new(ClaimTypes.NameIdentifier, Caller.ToString())],
                            "TestAuth"
                        )
                    ),
                },
            },
        };
}

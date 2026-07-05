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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves <c>GET /channels/moderated</c> resolves "the channels I moderate" from the CALLER's own channel (their
/// identity), not from a <c>broadcaster_id</c> claim the JWT never mints — the defect that made this endpoint
/// always answer 401. Asserts the mapped DTO shape, the DB-backed onboarded flag, the resolution path, and that a
/// caller with no owned channel gets an empty 200 rather than the old 401.
/// </summary>
public sealed class ChannelsControllerModeratedTests
{
    private static readonly Guid Caller = Guid.Parse("0192a000-0000-7000-8000-000000000e01");
    private static readonly Guid OwnChannel = Guid.Parse("0192a000-0000-7000-8000-000000000e02");

    [Fact]
    public async Task GetModeratedChannels_resolves_the_callers_own_channel_and_maps_results()
    {
        (
            ChannelsController controller,
            ITwitchModeratorsApi moderators,
            IChannelAccessService access,
            ApiTestDbContext db
        ) = Build();

        access
            .ResolveOwnChannelAsync(Caller.ToString(), Arg.Any<CancellationToken>())
            .Returns(OwnChannel);
        moderators
            .GetModeratedChannelsAsync(
                OwnChannel,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchModeratedChannel>(
                        [
                            new TwitchModeratedChannel("999", "coolstreamer", "CoolStreamer"),
                            new TwitchModeratedChannel("888", "otherstreamer", "OtherStreamer"),
                        ],
                        NextCursor: null,
                        Total: 2
                    )
                )
            );

        // "999" is already onboarded in our DB; "888" is not — the DTO's IsOnboarded must reflect that.
        db.Channels.Add(
            new Channel
            {
                Id = OwnChannel,
                OwnerUserId = Caller,
                TwitchChannelId = "999",
                Name = "coolstreamer",
                NameNormalized = "coolstreamer",
                IsOnboarded = true,
            }
        );
        await db.SaveChangesAsync();

        IActionResult result = await controller.GetModeratedChannels(default);

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<List<ChannelsController.ModeratedChannelDto>> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<List<ChannelsController.ModeratedChannelDto>>>()
            .Subject;

        body.Data.Should().HaveCount(2);
        body.Data.Should()
            .ContainSingle(d => d.Id == "999")
            .Which.Should()
            .BeEquivalentTo(
                new
                {
                    Login = "coolstreamer",
                    DisplayName = "CoolStreamer",
                    IsOnboarded = true,
                }
            );
        body.Data.Should().ContainSingle(d => d.Id == "888").Which.IsOnboarded.Should().BeFalse();

        // It resolved via the caller's OWN channel Guid — never a (missing) broadcaster_id claim.
        await moderators
            .Received(1)
            .GetModeratedChannelsAsync(
                OwnChannel,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task GetModeratedChannels_with_no_owned_channel_returns_empty_not_401()
    {
        (
            ChannelsController controller,
            ITwitchModeratorsApi moderators,
            IChannelAccessService access,
            _
        ) = Build();
        access
            .ResolveOwnChannelAsync(Caller.ToString(), Arg.Any<CancellationToken>())
            .Returns(Guid.Empty);

        IActionResult result = await controller.GetModeratedChannels(default);

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<List<ChannelsController.ModeratedChannelDto>> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<List<ChannelsController.ModeratedChannelDto>>>()
            .Subject;
        body.Data.Should().BeEmpty();

        // No owned channel → the moderators API is never queried, and the caller is NOT rejected (old 401 bug).
        await moderators
            .DidNotReceiveWithAnyArgs()
            .GetModeratedChannelsAsync(default, default!, default);
    }

    private static (
        ChannelsController Controller,
        ITwitchModeratorsApi Moderators,
        IChannelAccessService Access,
        ApiTestDbContext Db
    ) Build()
    {
        IChannelService service = Substitute.For<IChannelService>();
        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();
        IChannelAccessService access = Substitute.For<IChannelAccessService>();
        ApiTestDbContext db = ApiTestDbContext.New();

        ChannelsController controller = new(service, db, moderators, access)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity(
                            [new Claim(ClaimTypes.NameIdentifier, Caller.ToString())],
                            "TestAuth"
                        )
                    ),
                },
            },
        };
        return (controller, moderators, access, db);
    }
}

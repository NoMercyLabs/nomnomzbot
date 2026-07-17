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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Analytics.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves <see cref="CommunityController.ListMembers"/> fills each member's <c>WatchHours</c>/<c>CommandsUsed</c>
/// from the per-viewer-per-channel <see cref="ViewerProfile"/> aggregate — closing the bug where those two fields
/// were hardcoded to 0 (fake data) despite the real totals living on <c>ViewerProfile</c>. A member with no profile
/// row keeps 0/0, which is truthful (no analytics folded yet), not a lie.
/// </summary>
public sealed class CommunityControllerTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();

    private static CommunityController Build(
        CommunityControllerTestDbContext db,
        ITwitchChannelsApi channels
    ) =>
        new(
            db,
            channels,
            Substitute.For<ITwitchModeratorsApi>(),
            Substitute.For<ITwitchSubscriptionsApi>(),
            Substitute.For<ITwitchModerationApi>(),
            TimeProvider.System,
            Substitute.For<ICommunityStandingService>(),
            Substitute.For<ICurrentUserService>()
        );

    [Fact]
    public async Task ListMembers_fills_watch_hours_and_commands_used_from_the_viewer_profile_aggregate()
    {
        CommunityControllerTestDbContext db = CommunityControllerTestDbContext.New();

        // A followed viewer WITH analytics: 7200 watch seconds (= 2 whole hours) and 5 commands used.
        db.Users.Add(
            new User
            {
                Id = Guid.CreateVersion7(),
                TwitchUserId = "twitch-777",
                Username = "regular_raider",
                UsernameNormalized = "regular_raider",
                DisplayName = "Regular_Raider",
            }
        );
        db.ViewerProfiles.Add(
            new ViewerProfile
            {
                BroadcasterId = Broadcaster,
                ViewerUserId = Guid.CreateVersion7(),
                ViewerTwitchUserId = "twitch-777",
                TotalWatchSeconds = 7200,
                TotalCommandsUsed = 5,
                TotalMessages = 42,
            }
        );
        // A followed viewer with NO profile row — must stay 0/0 (truthful: no analytics yet).
        db.Users.Add(
            new User
            {
                Id = Guid.CreateVersion7(),
                TwitchUserId = "twitch-888",
                Username = "fresh_follow",
                UsernameNormalized = "fresh_follow",
                DisplayName = "Fresh_Follow",
            }
        );
        await db.SaveChangesAsync();

        ITwitchChannelsApi channels = Substitute.For<ITwitchChannelsApi>();
        channels
            .GetChannelFollowersAsync(
                Arg.Any<Guid>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchChannelFollower>(
                        [
                            new TwitchChannelFollower(
                                "twitch-777",
                                "regular_raider",
                                "Regular_Raider",
                                new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)
                            ),
                            new TwitchChannelFollower(
                                "twitch-888",
                                "fresh_follow",
                                "Fresh_Follow",
                                new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)
                            ),
                        ],
                        NextCursor: null,
                        Total: 2
                    )
                )
            );

        CommunityController controller = Build(db, channels);

        IActionResult result = await controller.ListMembers(
            Broadcaster.ToString(),
            new PageRequestDto { Take = 25 },
            role: "follower",
            cursor: null,
            CancellationToken.None
        );

        List<CommunityController.CommunityUserDto> members = Data(result);

        CommunityController.CommunityUserDto withProfile = members.Single(m =>
            m.Id == "twitch-777"
        );
        // The consequence of the fix: real aggregate values, not the old hardcoded 0.
        withProfile.WatchHours.Should().Be(2); // 7200s / 3600 = 2 whole hours
        withProfile.CommandsUsed.Should().Be(5);

        CommunityController.CommunityUserDto withoutProfile = members.Single(m =>
            m.Id == "twitch-888"
        );
        // No profile row → truthfully 0/0, not fabricated.
        withoutProfile.WatchHours.Should().Be(0);
        withoutProfile.CommandsUsed.Should().Be(0);
    }

    private static List<CommunityController.CommunityUserDto> Data(IActionResult result)
    {
        result.Should().BeOfType<OkObjectResult>();
        OkObjectResult ok = (OkObjectResult)result;
        PaginatedResponse<CommunityController.CommunityUserDto> body =
            (PaginatedResponse<CommunityController.CommunityUserDto>)ok.Value!;
        return body.Data.ToList();
    }
}

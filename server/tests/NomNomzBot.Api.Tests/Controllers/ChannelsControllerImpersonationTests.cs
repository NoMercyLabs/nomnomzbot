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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The channel list lazily grants a Moderator membership on every onboarded channel the caller moderates on
/// Twitch. Under act-as that grant must never run: an operator viewing the list as someone may not write role
/// rows on that person's behalf. The same request without impersonation proves the grant path is real, so the
/// act-as case fails for the right reason.
/// </summary>
public sealed class ChannelsControllerImpersonationTests
{
    private static readonly Guid Target = Guid.Parse("0192a000-0000-7000-8000-000000000f01");
    private static readonly Guid TargetOwnChannel = Guid.Parse(
        "0192a000-0000-7000-8000-000000000f02"
    );
    private static readonly Guid ModeratedChannel = Guid.Parse(
        "0192a000-0000-7000-8000-000000000f03"
    );

    [Fact]
    public async Task ListChannels_under_act_as_writes_no_moderator_membership()
    {
        (ChannelsController controller, IMembershipService memberships) = await BuildAsync(
            new ImpersonationContext(Guid.NewGuid(), Target, Guid.NewGuid())
        );

        IActionResult result = await controller.ListChannels(new(), default);

        result.Should().BeOfType<OkObjectResult>();
        await memberships
            .DidNotReceiveWithAnyArgs()
            .SetManagementRoleAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task ListChannels_for_the_user_themself_grants_the_moderator_membership()
    {
        (ChannelsController controller, IMembershipService memberships) = await BuildAsync(null);

        await controller.ListChannels(new(), default);

        await memberships
            .Received(1)
            .SetManagementRoleAsync(
                ModeratedChannel,
                Target,
                ManagementRole.Moderator,
                MembershipSource.TwitchBadge,
                null,
                Arg.Any<CancellationToken>()
            );
    }

    private static async Task<(ChannelsController, IMembershipService)> BuildAsync(
        ImpersonationContext? impersonation
    )
    {
        ApiTestDbContext db = ApiTestDbContext.New();
        db.Channels.Add(
            new()
            {
                Id = ModeratedChannel,
                OwnerUserId = Guid.NewGuid(),
                TwitchChannelId = "tw-moderated",
                Name = "moderated",
                NameNormalized = "moderated",
                IsOnboarded = true,
            }
        );
        await db.SaveChangesAsync();

        IChannelService channels = Substitute.For<IChannelService>();
        channels
            .GetChannelsAsync(
                Target.ToString(),
                Arg.Any<PaginationParams>(),
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new PagedList<ChannelSummaryDto>([], 1, 25, 0)));
        IChannelAccessService access = Substitute.For<IChannelAccessService>();
        access
            .ResolveOwnChannelAsync(Target.ToString(), Arg.Any<CancellationToken>())
            .Returns(TargetOwnChannel);
        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();
        moderators
            .GetModeratedChannelsAsync(
                TargetOwnChannel,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchModeratedChannel>(
                        [new("tw-moderated", "moderated", "Moderated")],
                        NextCursor: null,
                        Total: 1
                    )
                )
            );
        IMembershipService memberships = Substitute.For<IMembershipService>();
        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        currentUser.Impersonation.Returns(impersonation);

        ChannelsController controller = new(
            channels,
            db,
            moderators,
            access,
            memberships,
            Substitute.For<IUserService>(),
            Substitute.For<IChannelDeletePreviewService>(),
            currentUser
        )
        {
            ControllerContext = new()
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new(
                        new ClaimsIdentity(
                            [new(ClaimTypes.NameIdentifier, Target.ToString())],
                            "TestAuth"
                        )
                    ),
                },
            },
        };
        return (controller, memberships);
    }
}

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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves channel onboarding is self-scoped: the body's <c>BroadcasterId</c> is an OWNER USER id and
/// onboarding fires the full seed fan-out (EventSub subscribe, bot join, default commands), so a caller
/// naming ANOTHER user's id is 403'd before the service runs — only the caller themselves (or a platform
/// admin) onboards a channel.
/// </summary>
public sealed class ChannelsControllerOnboardTests
{
    private static readonly Guid Caller = Guid.Parse("0192a000-0000-7000-8000-000000000d01");
    private static readonly Guid Victim = Guid.Parse("0192a000-0000-7000-8000-000000000d02");

    private static (ChannelsController Controller, IChannelService Service) Build(
        bool asAdmin = false
    )
    {
        IChannelService service = Substitute.For<IChannelService>();
        List<Claim> claims = [new Claim(ClaimTypes.NameIdentifier, Caller.ToString())];
        if (asAdmin)
            claims.Add(new Claim(ClaimTypes.Role, "admin"));

        ChannelsController controller = new(
            service,
            Substitute.For<IApplicationDbContext>(),
            Substitute.For<ITwitchModeratorsApi>(),
            Substitute.For<IChannelAccessService>()
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")),
                },
            },
        };
        return (controller, service);
    }

    private static ChannelDto Channel(Guid ownerUserId) =>
        new(
            Guid.NewGuid().ToString(),
            "chan",
            "Chan",
            null,
            false,
            true,
            null,
            null,
            null,
            null,
            "free",
            null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        );

    [Fact]
    public async Task Onboarding_another_users_channel_returns_403_and_never_hits_the_service()
    {
        (ChannelsController controller, IChannelService service) = Build();

        IActionResult result = await controller.OnboardChannel(
            new CreateChannelRequest { BroadcasterId = Victim.ToString() },
            CancellationToken.None
        );

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        await service
            .DidNotReceive()
            .OnboardAsync(
                Arg.Any<string>(),
                Arg.Any<CreateChannelRequest>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Onboarding_your_own_channel_reaches_the_service_and_returns_201()
    {
        (ChannelsController controller, IChannelService service) = Build();
        service
            .OnboardAsync(
                Caller.ToString(),
                Arg.Any<CreateChannelRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(Channel(Caller)));

        IActionResult result = await controller.OnboardChannel(
            new CreateChannelRequest { BroadcasterId = Caller.ToString() },
            CancellationToken.None
        );

        result.Should().BeOfType<CreatedAtActionResult>();
        await service
            .Received(1)
            .OnboardAsync(
                Caller.ToString(),
                Arg.Any<CreateChannelRequest>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_platform_admin_may_onboard_on_behalf_of_another_user()
    {
        (ChannelsController controller, IChannelService service) = Build(asAdmin: true);
        service
            .OnboardAsync(
                Victim.ToString(),
                Arg.Any<CreateChannelRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(Channel(Victim)));

        IActionResult result = await controller.OnboardChannel(
            new CreateChannelRequest { BroadcasterId = Victim.ToString() },
            CancellationToken.None
        );

        result.Should().BeOfType<CreatedAtActionResult>();
    }
}

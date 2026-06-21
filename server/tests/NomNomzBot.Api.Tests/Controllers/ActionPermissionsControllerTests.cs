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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the action-permissions controller wires HTTP to <see cref="IActionAuthorizationService"/>
/// (roles-permissions §5): SetOverride passes the action key + level + authenticated caller and returns the
/// mapped result (a below-floor rejection surfaces as 400); a malformed channel id is rejected.
/// </summary>
public sealed class ActionPermissionsControllerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000e01");
    private static readonly Guid Caller = Guid.Parse("0192a000-0000-7000-8000-000000000e02");

    private static (
        ActionPermissionsController Controller,
        IActionAuthorizationService Service
    ) Build()
    {
        IActionAuthorizationService service = Substitute.For<IActionAuthorizationService>();
        ICurrentUserService user = Substitute.For<ICurrentUserService>();
        user.UserId.Returns(Caller.ToString());
        return (new ActionPermissionsController(service, user), service);
    }

    [Fact]
    public async Task SetOverride_passes_caller_and_returns_ok()
    {
        (ActionPermissionsController controller, IActionAuthorizationService service) = Build();
        service
            .SetActionOverrideAsync(
                Channel,
                "economy:config:write",
                30,
                Caller,
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(30));

        IActionResult result = await controller.SetOverride(
            Channel.ToString(),
            "economy:config:write",
            new ActionPermissionsController.SetOverrideBody(30),
            default
        );

        result.Should().BeOfType<OkObjectResult>();
        await service
            .Received(1)
            .SetActionOverrideAsync(
                Channel,
                "economy:config:write",
                30,
                Caller,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task SetOverride_maps_a_below_floor_rejection_to_400()
    {
        (ActionPermissionsController controller, IActionAuthorizationService service) = Build();
        service
            .SetActionOverrideAsync(
                Channel,
                "moderation:ban",
                4,
                Caller,
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<int>("below floor", "VALIDATION_FAILED"));

        IActionResult result = await controller.SetOverride(
            Channel.ToString(),
            "moderation:ban",
            new ActionPermissionsController.SetOverrideBody(4),
            default
        );

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Matrix_rejects_a_malformed_channel_id()
    {
        (ActionPermissionsController controller, _) = Build();

        IActionResult result = await controller.Matrix("not-a-guid", default);

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}

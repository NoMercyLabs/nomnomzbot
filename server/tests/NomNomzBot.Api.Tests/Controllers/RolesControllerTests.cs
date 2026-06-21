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
using NomNomzBot.Domain.Identity.Enums;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the roles controller wires HTTP to <see cref="IMembershipService"/> (roles-permissions §5): SetRole
/// passes the authenticated caller as the grantor with a manual (BotGrant) source and returns the mapped
/// result; a malformed channel id is rejected.
/// </summary>
public sealed class RolesControllerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000d01");
    private static readonly Guid Caller = Guid.Parse("0192a000-0000-7000-8000-000000000d02");
    private static readonly Guid Target = Guid.Parse("0192a000-0000-7000-8000-000000000d03");

    private static (RolesController Controller, IMembershipService Service) Build()
    {
        IMembershipService service = Substitute.For<IMembershipService>();
        ICurrentUserService user = Substitute.For<ICurrentUserService>();
        user.UserId.Returns(Caller.ToString());
        return (new RolesController(service, user), service);
    }

    [Fact]
    public async Task SetRole_passes_caller_and_botgrant_source_and_returns_ok()
    {
        (RolesController controller, IMembershipService service) = Build();
        ChannelMembershipDto dto = new(
            Guid.Parse("0192a000-0000-7000-8000-000000000d04"),
            Target,
            "target",
            ManagementRole.Editor,
            30,
            MembershipSource.BotGrant,
            Caller,
            default,
            null
        );
        service
            .SetManagementRoleAsync(
                Channel,
                Target,
                ManagementRole.Editor,
                MembershipSource.BotGrant,
                Caller,
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(dto));

        IActionResult result = await controller.SetRole(
            Channel.ToString(),
            new RolesController.SetRoleBody(Target, ManagementRole.Editor),
            default
        );

        result.Should().BeOfType<OkObjectResult>();
        await service
            .Received(1)
            .SetManagementRoleAsync(
                Channel,
                Target,
                ManagementRole.Editor,
                MembershipSource.BotGrant,
                Caller,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task List_rejects_a_malformed_channel_id()
    {
        (RolesController controller, _) = Build();

        IActionResult result = await controller.List("not-a-guid", 1, 25, default);

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}

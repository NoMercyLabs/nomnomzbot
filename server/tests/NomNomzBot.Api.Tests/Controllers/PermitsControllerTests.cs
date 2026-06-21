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
/// Proves the permits controller wires HTTP to <see cref="IPermitService"/> (roles-permissions §5): it parses
/// the channel id, passes the authenticated caller as the grantor, returns the mapped result, and rejects a
/// malformed channel id.
/// </summary>
public sealed class PermitsControllerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000c01");
    private static readonly Guid Caller = Guid.Parse("0192a000-0000-7000-8000-000000000c02");
    private static readonly Guid Target = Guid.Parse("0192a000-0000-7000-8000-000000000c03");

    private static (PermitsController Controller, IPermitService Service) Build()
    {
        IPermitService service = Substitute.For<IPermitService>();
        ICurrentUserService user = Substitute.For<ICurrentUserService>();
        user.UserId.Returns(Caller.ToString());
        return (new PermitsController(service, user), service);
    }

    [Fact]
    public async Task GrantRole_passes_the_caller_as_grantor_and_returns_ok()
    {
        (PermitsController controller, IPermitService service) = Build();
        PermitGrantDto dto = new(
            Guid.Parse("0192a000-0000-7000-8000-000000000c04"),
            Target,
            null,
            PermitGrantType.Role,
            ManagementRole.Editor,
            null,
            Caller,
            null,
            null,
            null,
            default
        );
        service
            .GrantRoleAsync(
                Channel,
                Target,
                ManagementRole.Editor,
                Caller,
                Arg.Any<DateTime?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(dto));

        IActionResult result = await controller.GrantRole(
            Channel.ToString(),
            new PermitsController.GrantRoleBody(Target, ManagementRole.Editor, null, "guest"),
            default
        );

        result.Should().BeOfType<OkObjectResult>();
        await service
            .Received(1)
            .GrantRoleAsync(
                Channel,
                Target,
                ManagementRole.Editor,
                Caller,
                null,
                "guest",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task GrantCapability_maps_a_forbidden_result_to_403()
    {
        (PermitsController controller, IPermitService service) = Build();
        service
            .GrantCapabilityAsync(
                Channel,
                Target,
                "roles:manage",
                Caller,
                Arg.Any<DateTime?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<PermitGrantDto>("not delegable", "FORBIDDEN"));

        IActionResult result = await controller.GrantCapability(
            Channel.ToString(),
            new PermitsController.GrantCapabilityBody(Target, "roles:manage", null, null),
            default
        );

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task List_rejects_a_malformed_channel_id()
    {
        (PermitsController controller, _) = Build();

        IActionResult result = await controller.List("not-a-guid", default);

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}

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
        IRoleResolver resolver = Substitute.For<IRoleResolver>();
        ICurrentUserService user = Substitute.For<ICurrentUserService>();
        user.UserId.Returns(Caller.ToString());
        return (new RolesController(service, resolver, user), service);
    }

    private static (RolesController Controller, IRoleResolver Resolver) BuildWithResolver()
    {
        IMembershipService service = Substitute.For<IMembershipService>();
        IRoleResolver resolver = Substitute.For<IRoleResolver>();
        ICurrentUserService user = Substitute.For<ICurrentUserService>();
        user.UserId.Returns(Caller.ToString());
        return (new RolesController(service, resolver, user), resolver);
    }

    private static ResolvedAccessDto AccessFor(Guid userId, ManagementRole? role, int level) =>
        new(
            userId,
            Channel,
            level,
            CommunityStanding.Everyone,
            0,
            role,
            role?.ToLevel() ?? 0,
            null,
            [],
            level == 0 ? "community" : "management"
        );

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

    [Fact]
    public async Task List_returns_the_flat_paginated_shape_the_dashboard_deserializes()
    {
        (RolesController controller, IMembershipService service) = Build();
        ChannelMembershipDto member = new(
            Guid.NewGuid(),
            Target,
            "nibbles",
            ManagementRole.Editor,
            30,
            MembershipSource.BotGrant,
            Caller,
            default,
            null
        );
        service
            .ListMembershipsAsync(Channel, 1, 100, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new PagedList<ChannelMembershipDto>([member], 1, 100, 1)));

        IActionResult result = await controller.List(Channel.ToString(), 1, 100, default);

        // The dashboard's PaginatedEnvelope reads `data` as the array, so the body MUST be the flat
        // PaginatedResponse ({ data: [...] }) — NOT StatusResponseDto<PagedList> ({ data: { items: [...] } }),
        // the exact shape mismatch that crashed the roles page on load.
        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        PaginatedResponse<ChannelMembershipDto> body = ok
            .Value.Should()
            .BeOfType<PaginatedResponse<ChannelMembershipDto>>()
            .Subject;
        body.Data.Should().ContainSingle().Which.UserId.Should().Be(Target);
    }

    [Fact]
    public async Task EffectiveMe_resolves_the_authenticated_caller_not_an_arbitrary_user()
    {
        // /effective/me is the shell's self-introspection on session establish — it must resolve the CALLER's
        // own access, so the resolver is asked about the authenticated caller (never a path-supplied id).
        (RolesController controller, IRoleResolver resolver) = BuildWithResolver();
        ResolvedAccessDto access = AccessFor(Caller, ManagementRole.Editor, 30);
        resolver
            .ResolveAccessAsync(Caller, Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Success(access));

        IActionResult result = await controller.EffectiveMe(Channel.ToString(), default);

        result.Should().BeOfType<OkObjectResult>();
        StatusResponseDto<ResolvedAccessDto> body =
            (StatusResponseDto<ResolvedAccessDto>)((OkObjectResult)result).Value!;
        body.Data!.UserId.Should().Be(Caller);
        body.Data!.ManagementRole.Should().Be(ManagementRole.Editor);
        body.Data!.EffectiveLevel.Should().Be(30);
        await resolver
            .Received(1)
            .ResolveAccessAsync(Caller, Channel, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EffectiveMe_returns_a_viewer_with_no_management_role_for_a_role_less_caller()
    {
        // A pure viewer (no Plane-B role) must be able to learn they have NO management access — the shell
        // routes them to the participation-only surface off this exact result.
        (RolesController controller, IRoleResolver resolver) = BuildWithResolver();
        resolver
            .ResolveAccessAsync(Caller, Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Success(AccessFor(Caller, null, 0)));

        IActionResult result = await controller.EffectiveMe(Channel.ToString(), default);

        StatusResponseDto<ResolvedAccessDto> body =
            (StatusResponseDto<ResolvedAccessDto>)((OkObjectResult)result).Value!;
        body.Data!.ManagementRole.Should().BeNull();
        body.Data!.EffectiveLevel.Should().Be(0);
    }

    [Fact]
    public async Task EffectiveMe_rejects_a_malformed_channel_id()
    {
        (RolesController controller, _) = BuildWithResolver();

        IActionResult result = await controller.EffectiveMe("not-a-guid", default);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Effective_resolves_the_path_user_on_the_channel()
    {
        // The sibling /effective/{userId} (roles:read floor) resolves an arbitrary channel member's access.
        (RolesController controller, IRoleResolver resolver) = BuildWithResolver();
        resolver
            .ResolveAccessAsync(Target, Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Success(AccessFor(Target, ManagementRole.Moderator, 10)));

        IActionResult result = await controller.Effective(Channel.ToString(), Target, default);

        result.Should().BeOfType<OkObjectResult>();
        await resolver
            .Received(1)
            .ResolveAccessAsync(Target, Channel, Arg.Any<CancellationToken>());
    }
}

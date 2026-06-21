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
using Microsoft.AspNetCore.Authorization;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Authorization;

/// <summary>
/// Proves Gate-2 enforcement at the authorization boundary (roles-permissions §6): the handler succeeds only
/// when the caller is authenticated, a tenant is resolved, AND <see cref="IActionAuthorizationService"/>
/// allows the action — and short-circuits (without calling Gate 2) when unauthenticated or tenantless.
/// </summary>
public sealed class ActionAuthorizationHandlerTests
{
    private static readonly Guid User = Guid.Parse("0192a000-0000-7000-8000-000000000b01");
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000b02");
    private const string Action = "economy:config:write";

    private static AuthorizationHandlerContext Context(ActionAuthorizationRequirement requirement)
    {
        ClaimsPrincipal principal = new(new ClaimsIdentity(authenticationType: "test"));
        return new AuthorizationHandlerContext([requirement], principal, resource: null);
    }

    private static (ActionAuthorizationHandler Handler, IActionAuthorizationService Gate2) Build(
        bool authenticated = true,
        Guid? tenant = null,
        bool gate2Allows = false
    )
    {
        IActionAuthorizationService gate2 = Substitute.For<IActionAuthorizationService>();
        gate2
            .AuthorizeActionAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(gate2Allows));

        ICurrentUserService user = Substitute.For<ICurrentUserService>();
        user.IsAuthenticated.Returns(authenticated);
        user.UserId.Returns(authenticated ? User.ToString() : null);

        ICurrentTenantService tenantService = Substitute.For<ICurrentTenantService>();
        tenantService.BroadcasterId.Returns(tenant);

        return (new ActionAuthorizationHandler(gate2, user, tenantService), gate2);
    }

    [Fact]
    public async Task Succeeds_when_authenticated_tenant_resolved_and_gate2_allows()
    {
        (ActionAuthorizationHandler handler, _) = Build(tenant: Channel, gate2Allows: true);
        AuthorizationHandlerContext context = Context(new ActionAuthorizationRequirement(Action));

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Fails_when_gate2_denies()
    {
        (ActionAuthorizationHandler handler, _) = Build(tenant: Channel, gate2Allows: false);
        AuthorizationHandlerContext context = Context(new ActionAuthorizationRequirement(Action));

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task Fails_and_skips_gate2_when_unauthenticated()
    {
        (ActionAuthorizationHandler handler, IActionAuthorizationService gate2) = Build(
            authenticated: false,
            tenant: Channel,
            gate2Allows: true
        );
        AuthorizationHandlerContext context = Context(new ActionAuthorizationRequirement(Action));

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
        await gate2
            .DidNotReceive()
            .AuthorizeActionAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Fails_and_skips_gate2_when_no_tenant_resolved()
    {
        (ActionAuthorizationHandler handler, IActionAuthorizationService gate2) = Build(
            tenant: null,
            gate2Allows: true
        );
        AuthorizationHandlerContext context = Context(new ActionAuthorizationRequirement(Action));

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
        await gate2
            .DidNotReceive()
            .AuthorizeActionAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }
}

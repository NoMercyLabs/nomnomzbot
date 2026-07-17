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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the users surface is self-or-Gate-2 (reads) and strictly self-only (profile write): a foreign caller
/// without <c>community:read</c> is 403'd and the service is NEVER invoked; the subject themselves always
/// passes; a manager holding <c>community:read</c> on the tenant may read (but still not write) another user.
/// </summary>
public sealed class UsersControllerAuthorizationTests
{
    private static readonly Guid Caller = Guid.Parse("0192a000-0000-7000-8000-000000000c01");
    private static readonly Guid OtherUser = Guid.Parse("0192a000-0000-7000-8000-000000000c02");
    private static readonly Guid Tenant = Guid.Parse("0192a000-0000-7000-8000-000000000c03");

    private sealed record Fixture(
        UsersController Controller,
        IUserService Users,
        IActionAuthorizationService Gate2
    );

    private static Fixture Build(bool gate2Allows = false, bool tenantResolved = true)
    {
        IUserService users = Substitute.For<IUserService>();
        IActionAuthorizationService gate2 = Substitute.For<IActionAuthorizationService>();
        gate2
            .AuthorizeActionAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(gate2Allows));

        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(Caller.ToString());

        ICurrentTenantService currentTenant = Substitute.For<ICurrentTenantService>();
        currentTenant.BroadcasterId.Returns(tenantResolved ? Tenant : null);

        UsersController controller = new(users, gate2, currentUser, currentTenant)
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
        return new Fixture(controller, users, gate2);
    }

    private static UserProfileDto Profile(Guid id) =>
        new(
            id.ToString(),
            "user",
            "User",
            null,
            null,
            null,
            null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
        );

    // ── PUT {userId}/profile — strictly self-only ──────────────────────────────

    [Fact]
    public async Task UpdateUserProfile_for_another_user_returns_403_and_never_hits_the_service()
    {
        Fixture f = Build(gate2Allows: true); // even a caller who could READ via Gate 2 must not WRITE

        IActionResult result = await f.Controller.UpdateUserProfile(
            OtherUser.ToString(),
            new UpdateUserProfileRequest { DisplayName = "hijacked" },
            CancellationToken.None
        );

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        await f
            .Users.DidNotReceive()
            .UpdateProfileAsync(
                Arg.Any<string>(),
                Arg.Any<UpdateUserProfileRequest>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task UpdateUserProfile_for_self_reaches_the_service_and_returns_the_updated_profile()
    {
        Fixture f = Build();
        f.Users.UpdateProfileAsync(
                Caller.ToString(),
                Arg.Any<UpdateUserProfileRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(Profile(Caller)));

        IActionResult result = await f.Controller.UpdateUserProfile(
            Caller.ToString(),
            new UpdateUserProfileRequest { DisplayName = "Me" },
            CancellationToken.None
        );

        result.Should().BeOfType<OkObjectResult>();
        await f
            .Users.Received(1)
            .UpdateProfileAsync(
                Caller.ToString(),
                Arg.Any<UpdateUserProfileRequest>(),
                Arg.Any<CancellationToken>()
            );
    }

    // ── GET {userId}/profile — self-or-Gate-2 ──────────────────────────────────

    [Fact]
    public async Task GetUserProfile_of_another_user_without_gate2_returns_403_and_never_hits_the_service()
    {
        Fixture f = Build(gate2Allows: false);

        IActionResult result = await f.Controller.GetUserProfile(
            OtherUser.ToString(),
            CancellationToken.None
        );

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        await f
            .Users.DidNotReceive()
            .GetProfileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetUserProfile_of_self_succeeds_without_any_gate2_check()
    {
        Fixture f = Build(gate2Allows: false);
        f.Users.GetProfileAsync(Caller.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Profile(Caller)));

        IActionResult result = await f.Controller.GetUserProfile(
            Caller.ToString(),
            CancellationToken.None
        );

        result.Should().BeOfType<OkObjectResult>();
        await f
            .Gate2.DidNotReceive()
            .AuthorizeActionAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task GetUserProfile_of_another_user_with_community_read_succeeds()
    {
        Fixture f = Build(gate2Allows: true);
        f.Users.GetProfileAsync(OtherUser.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Profile(OtherUser)));

        IActionResult result = await f.Controller.GetUserProfile(
            OtherUser.ToString(),
            CancellationToken.None
        );

        result.Should().BeOfType<OkObjectResult>();
        await f
            .Gate2.Received(1)
            .AuthorizeActionAsync(Caller, Tenant, "community:read", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetUser_of_another_user_without_a_resolved_tenant_returns_403()
    {
        // No tenant context (fresh account, no X-Channel-Id) → the Gate-2 arm cannot run → deny.
        Fixture f = Build(gate2Allows: true, tenantResolved: false);

        IActionResult result = await f.Controller.GetUser(
            OtherUser.ToString(),
            CancellationToken.None
        );

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        await f.Users.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}

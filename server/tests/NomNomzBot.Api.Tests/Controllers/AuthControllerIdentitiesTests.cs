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
using Microsoft.Extensions.Configuration;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NSubstitute;
using Xunit;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// <c>GET auth/identities</c> (platform-identity §5) is self-scoped: it lists the identities of the JWT
/// <c>sub</c> only, and rejects a request with no usable <c>sub</c> claim.
/// </summary>
public sealed class AuthControllerIdentitiesTests
{
    private static AuthController Build(IUserIdentityService identities, ClaimsPrincipal user) =>
        new(
            Substitute.For<IUserService>(),
            Substitute.For<IAuthService>(),
            new ConfigurationBuilder().Build(),
            TimeProvider.System,
            Substitute.For<ITwitchOAuthStateService>(),
            Substitute.For<ILoginProviderRegistry>(),
            identities,
            Array.Empty<ILoginIdentityProvider>(),
            Array.Empty<IAuthCodeLoginProvider>(),
            Substitute.For<IExternalLoginService>()
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user },
            },
        };

    [Fact]
    public async Task GetMyIdentities_returns_the_callers_identities()
    {
        Guid userId = Guid.Parse("0192a000-0000-7000-8000-0000000000a1");
        UserIdentityDto identity = new(
            "twitch",
            "12345",
            "streamer",
            "Streamer",
            null,
            true,
            DateTime.UtcNow,
            null
        );
        IUserIdentityService svc = Substitute.For<IUserIdentityService>();
        svc.ListAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<UserIdentityDto>>([identity]));

        ClaimsPrincipal user = new(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test")
        );

        IActionResult result = await Build(svc, user).GetMyIdentities(default);

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<IReadOnlyList<UserIdentityDto>> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<IReadOnlyList<UserIdentityDto>>>()
            .Subject;
        body.Data.Should().ContainSingle().Which.Provider.Should().Be("twitch");
    }

    [Fact]
    public async Task GetMyIdentities_without_a_valid_sub_is_unauthorized()
    {
        AuthController controller = Build(
            Substitute.For<IUserIdentityService>(),
            new ClaimsPrincipal(new ClaimsIdentity())
        );

        IActionResult result = await controller.GetMyIdentities(default);

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }
}

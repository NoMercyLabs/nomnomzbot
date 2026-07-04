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
using Microsoft.Extensions.Hosting;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the platform-bot OAuth URL is locked once setup is complete: completing that OAuth flow RE-POINTS
/// the platform bot identity to whichever Twitch account authorizes, so post-setup only a platform admin may
/// mint it — an anonymous/ordinary caller is 403'd and no CSRF state is issued. During the first-run window
/// (setup incomplete) the endpoint stays open, because it IS the bootstrap path.
/// </summary>
public sealed class SystemControllerBotOAuthLockTests
{
    private static SystemController Build(
        bool setupComplete,
        bool asAdmin,
        out ITwitchOAuthStateService oauthState,
        out IAuthService authService
    )
    {
        authService = Substitute.For<IAuthService>();
        authService
            .GetTwitchBotOAuthUrl(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success("https://id.twitch.tv/oauth2/authorize?bot"));
        // Setup completeness derives from the explicit marker: system.setup_complete == "true".
        ISystemCredentialsProvider credentials = Substitute.For<ISystemCredentialsProvider>();
        credentials
            .GetValueAsync("system", "setup_complete", Arg.Any<CancellationToken>())
            .Returns(setupComplete ? "true" : null);

        oauthState = Substitute.For<ITwitchOAuthStateService>();
        oauthState
            .IssueAsync(Arg.Any<TwitchOAuthFlowState>(), Arg.Any<CancellationToken>())
            .Returns("state-nonce");

        List<Claim> claims = [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())];
        if (asAdmin)
            claims.Add(new Claim(ClaimTypes.Role, "admin"));

        return new SystemController(
            authService,
            ApiTestDbContext.New(),
            new ConfigurationBuilder().Build(),
            Substitute.For<ITokenProtector>(),
            credentials,
            Substitute.For<IHostEnvironment>(),
            oauthState
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
    }

    [Fact]
    public async Task After_setup_a_non_admin_caller_is_forbidden_and_no_state_is_issued()
    {
        SystemController controller = Build(
            setupComplete: true,
            asAdmin: false,
            out ITwitchOAuthStateService oauthState,
            out IAuthService authService
        );

        IActionResult result = await controller.GetBotOAuthUrl(CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        await oauthState
            .DidNotReceive()
            .IssueAsync(Arg.Any<TwitchOAuthFlowState>(), Arg.Any<CancellationToken>());
        await authService
            .DidNotReceive()
            .GetTwitchBotOAuthUrl(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task After_setup_a_platform_admin_still_gets_the_url()
    {
        SystemController controller = Build(
            setupComplete: true,
            asAdmin: true,
            out ITwitchOAuthStateService _,
            out IAuthService _
        );

        IActionResult result = await controller.GetBotOAuthUrl(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task During_the_first_run_window_the_url_is_minted_for_the_bootstrap_flow()
    {
        SystemController controller = Build(
            setupComplete: false,
            asAdmin: false,
            out ITwitchOAuthStateService oauthState,
            out IAuthService _
        );

        IActionResult result = await controller.GetBotOAuthUrl(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        await oauthState
            .Received(1)
            .IssueAsync(Arg.Any<TwitchOAuthFlowState>(), Arg.Any<CancellationToken>());
    }
}

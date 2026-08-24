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
/// Proves the setup bootstrap window is driven ONLY by the explicit <c>system.setup_complete</c> marker, never
/// inferred from "a Twitch app decision is recorded + the platform bot is authorized". Those two facts can both
/// be true while the operator is still mid-wizard on the optional steps (Spotify/Discord/YouTube) — inferring
/// completion from them closed the window early and 403'd the owner filling in his Spotify credentials before
/// the wizard's finish step ever ran. Once the marker IS recorded, the window closes exactly as before: only a
/// platform admin may still write credentials.
/// </summary>
public sealed class SystemControllerSetupWindowTests
{
    private static SystemController Build(bool setupCompleteMarker, bool asAdmin)
    {
        IAuthService authService = Substitute.For<IAuthService>();
        // Both onboarding facts a naive "system is ready" inference would key off are TRUE — the exact
        // shape of the owner's DB (a recorded Twitch decision + an authorized platform bot) — yet the
        // marker itself is what must gate the window.
        authService
            .GetBotStatusAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Success(new BotStatusDto(true, "NomNomzBot", null, null)));

        ISystemCredentialsProvider credentials = Substitute.For<ISystemCredentialsProvider>();
        credentials
            .GetClientIdAsync("twitch", Arg.Any<CancellationToken>())
            .Returns("twitch-public-id");
        credentials
            .GetAsync("twitch", Arg.Any<CancellationToken>())
            .Returns(new SystemAppCredentials("twitch-public-id", "sealed-secret"));
        credentials
            .IsAppDecisionRecordedAsync("twitch", Arg.Any<CancellationToken>())
            .Returns(true);
        credentials
            .GetValueAsync("system", "setup_complete", Arg.Any<CancellationToken>())
            .Returns(setupCompleteMarker ? "true" : null);

        List<Claim> claims = [new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())];
        if (asAdmin)
            claims.Add(new(ClaimTypes.Role, "admin"));

        return new(
            authService,
            ApiTestDbContext.New(),
            new ConfigurationBuilder().Build(),
            Substitute.For<ITokenProtector>(),
            credentials,
            Substitute.For<IHostEnvironment>(),
            Substitute.For<ITwitchOAuthStateService>()
        )
        {
            ControllerContext = new()
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new(new ClaimsIdentity(claims, "TestAuth")),
                },
            },
        };
    }

    [Fact]
    public async Task Anonymous_spotify_save_succeeds_mid_wizard_even_with_a_recorded_twitch_decision_and_bot()
    {
        SystemController controller = Build(setupCompleteMarker: false, asAdmin: false);

        IActionResult result = await controller.SaveSpotifyCredentials(
            new("spotify-id", "spotify-secret"),
            CancellationToken.None
        );

        result
            .Should()
            .BeOfType<OkObjectResult>(
                "the bootstrap window must stay open until the wizard's finish step records "
                    + "system.setup_complete — never inferred from Twitch decision + bot alone"
            );
    }

    [Fact]
    public async Task Anonymous_spotify_save_is_forbidden_once_setup_complete_is_recorded()
    {
        SystemController controller = Build(setupCompleteMarker: true, asAdmin: false);

        IActionResult result = await controller.SaveSpotifyCredentials(
            new("spotify-id", "spotify-secret"),
            CancellationToken.None
        );

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task An_admin_can_still_save_spotify_credentials_after_setup_is_complete()
    {
        SystemController controller = Build(setupCompleteMarker: true, asAdmin: true);

        IActionResult result = await controller.SaveSpotifyCredentials(
            new("spotify-id", "spotify-secret"),
            CancellationToken.None
        );

        result.Should().BeOfType<OkObjectResult>();
    }
}

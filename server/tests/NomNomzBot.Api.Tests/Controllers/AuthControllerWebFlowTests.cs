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
using Microsoft.Extensions.Configuration;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the served-web streamer-login completion (KMP onboarding gap 4): when the user flow started with
/// <c>client=web</c>, the Twitch callback returns a <b>302 to the served origin</b> carrying the access token in
/// the URL <c>#fragment</c> and the refresh token in an <c>HttpOnly</c>+<c>Secure</c>+<c>SameSite=Lax</c> cookie
/// — never a JSON body (a full-page redirect cannot read one). The default (non-web) caller still gets the
/// JSON token shape, so existing callback behavior is unchanged.
/// </summary>
public sealed class AuthControllerWebFlowTests
{
    private static readonly DateTime ExpiresAt = DateTime.UtcNow.AddHours(1);

    [Fact]
    public async Task WebCallback_Returns302_WithAccessTokenFragment_AndHttpOnlyRefreshCookie()
    {
        (AuthController controller, IAuthService auth, ITwitchOAuthStateService state) = Build();
        state
            .ConsumeAsync("nonce", Arg.Any<CancellationToken>())
            .Returns(new TwitchOAuthFlowState("user", RedirectUri: null, Client: "web"));
        auth.HandleTwitchCallbackAsync(
                Arg.Any<OAuthCallbackDto>(),
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(Auth("acc-tok", "ref-tok")));

        IActionResult result = await controller.HandleTwitchCallback("code", "nonce", default);

        // Full-page redirect back to the served origin with the access token in the fragment.
        RedirectResult redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().StartWith("https://dash.example.test/#access_token=");
        redirect.Url.Should().Contain("access_token=acc-tok");
        redirect.Url.Should().Contain("expires_in=");
        // The refresh token never rides in the URL.
        redirect.Url.Should().NotContain("ref-tok");

        // The refresh token is set as a hardened cookie the SPA's JS cannot read.
        string setCookie = controller.Response.Headers["Set-Cookie"].ToString().ToLowerInvariant();
        setCookie.Should().Contain("nnz_refresh_token=ref-tok");
        setCookie.Should().Contain("httponly");
        setCookie.Should().Contain("secure");
        setCookie.Should().Contain("samesite=lax");
    }

    [Fact]
    public async Task NonWebCallback_StillReturnsJsonTokenShape()
    {
        (AuthController controller, IAuthService auth, ITwitchOAuthStateService state) = Build();
        // No client class → the default web-SPA-less JSON contract (existing behavior).
        state
            .ConsumeAsync("nonce", Arg.Any<CancellationToken>())
            .Returns(new TwitchOAuthFlowState("user"));
        auth.HandleTwitchCallbackAsync(
                Arg.Any<OAuthCallbackDto>(),
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(Auth("acc-tok", "ref-tok")));

        IActionResult result = await controller.HandleTwitchCallback("code", "nonce", default);

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<StatusResponseDto<object>>();

        // No refresh cookie is set on the default path.
        controller
            .Response.Headers["Set-Cookie"]
            .ToString()
            .Should()
            .NotContain("nnz_refresh_token");
    }

    // ─── scaffolding ───────────────────────────────────────────────────────────

    private static AuthResultDto Auth(string access, string refresh) =>
        new(
            access,
            refresh,
            ExpiresAt,
            new UserDto(
                Guid.NewGuid().ToString(),
                "stoney",
                "Stoney",
                ProfileImageUrl: null,
                Email: null,
                CreatedAt: DateTime.UtcNow,
                LastLoginAt: DateTime.UtcNow
            )
        );

    private static (
        AuthController Controller,
        IAuthService Auth,
        ITwitchOAuthStateService State
    ) Build()
    {
        IUserService userService = Substitute.For<IUserService>();
        IAuthService authService = Substitute.For<IAuthService>();
        ITwitchOAuthStateService state = Substitute.For<ITwitchOAuthStateService>();
        // No explicit (non-loopback) App:BaseUrl, so the public origin resolves from the request host — the
        // served-web flow must land back on the exact origin the dashboard was served from (the tunnel/domain).
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["App:BaseUrl"] = "http://localhost:5080" }
            )
            .Build();

        DefaultHttpContext http = new();
        // ResolvePublicOrigin() falls to the request host when App:BaseUrl is loopback (the auto-set default).
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("dash.example.test");

        AuthController controller = new(
            userService,
            authService,
            config,
            TimeProvider.System,
            state
        )
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
        return (controller, authService, state);
    }
}

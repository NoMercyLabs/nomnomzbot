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
using Microsoft.AspNetCore.Http;
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
/// S098c — refresh-token custody is decided by whether the request carries the <c>nnz_refresh_token</c>
/// HttpOnly cookie, never by a caller-controlled <c>?client=</c> query flag: a cookie-borne refresh rotates the
/// cookie and strips the token from the body (web custody); a body-borne refresh (no cookie) returns the
/// rotated token in the body (native custody). Cookie-borne refresh additionally requires an allowed Origin —
/// the only CSRF defense beyond <c>SameSite=Lax</c> — while body-borne (native) refresh is exempt since it
/// carries no ambient browser credential to forge.
/// </summary>
public sealed class AuthControllerRefreshCustodyTests
{
    private static readonly DateTime ExpiresAt = DateTime.UtcNow.AddHours(1);

    [Fact]
    public async Task RequestWithCookie_RotatesCookie_AndOmitsRefreshTokenFromBody()
    {
        (AuthController controller, IAuthService auth) = Build(
            allowedOrigin: "https://dash.example.test"
        );
        controller.Request.Headers.Origin = "https://dash.example.test";
        controller.Request.Headers.Cookie = "nnz_refresh_token=old-ref-tok";
        auth.RefreshTokenAsync(
                "old-ref-tok",
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(Auth("new-acc-tok", "new-ref-tok")));

        IActionResult result = await controller.RefreshToken(null, null, default);

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        string body = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        body.Should().NotContain("new-ref-tok");
        body.Should().Contain("new-acc-tok");

        string setCookie = controller.Response.Headers["Set-Cookie"].ToString();
        setCookie.Should().Contain("nnz_refresh_token=new-ref-tok");
        setCookie.ToLowerInvariant().Should().Contain("httponly");
    }

    [Fact]
    public async Task RequestWithoutCookie_ReturnsRotatedRefreshTokenInBody_NoCookieSet()
    {
        (AuthController controller, IAuthService auth) = Build();
        auth.RefreshTokenAsync(
                "body-ref-tok",
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(Auth("new-acc-tok", "new-ref-tok")));

        IActionResult result = await controller.RefreshToken(
            new RefreshTokenRequest("body-ref-tok"),
            null,
            default
        );

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        string body = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        body.Should().Contain("new-ref-tok");

        controller.Response.Headers["Set-Cookie"].ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task CookieBorneRefresh_ForeignOrigin_IsRejected()
    {
        (AuthController controller, IAuthService auth) = Build(
            allowedOrigin: "https://dash.example.test"
        );
        controller.Request.Headers.Origin = "https://evil.example.net";
        controller.Request.Headers.Cookie = "nnz_refresh_token=old-ref-tok";

        IActionResult result = await controller.RefreshToken(null, null, default);

        result.Should().BeOfType<UnauthorizedObjectResult>();
        await auth.DidNotReceive()
            .RefreshTokenAsync(
                Arg.Any<string>(),
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task CookieBorneRefresh_AllowedOrigin_Succeeds()
    {
        (AuthController controller, IAuthService auth) = Build(
            allowedOrigin: "https://dash.example.test"
        );
        controller.Request.Headers.Origin = "https://dash.example.test";
        controller.Request.Headers.Cookie = "nnz_refresh_token=old-ref-tok";
        auth.RefreshTokenAsync(
                "old-ref-tok",
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(Auth("new-acc-tok", "new-ref-tok")));

        IActionResult result = await controller.RefreshToken(null, null, default);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task ClientQueryParameter_NeverChangesCustody()
    {
        // With the cookie present, custody must be identical whether ?client= is passed or not — it is no
        // longer consulted for the decision.
        (AuthController controllerA, IAuthService authA) = Build(
            allowedOrigin: "https://dash.example.test"
        );
        controllerA.Request.Headers.Origin = "https://dash.example.test";
        controllerA.Request.Headers.Cookie = "nnz_refresh_token=old-ref-tok";
        authA
            .RefreshTokenAsync(
                "old-ref-tok",
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(Auth("new-acc-tok", "new-ref-tok")));

        (AuthController controllerB, IAuthService authB) = Build(
            allowedOrigin: "https://dash.example.test"
        );
        controllerB.Request.Headers.Origin = "https://dash.example.test";
        controllerB.Request.Headers.Cookie = "nnz_refresh_token=old-ref-tok";
        authB
            .RefreshTokenAsync(
                "old-ref-tok",
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(Auth("new-acc-tok", "new-ref-tok")));

        IActionResult resultNoClient = await controllerA.RefreshToken(null, null, default);
        IActionResult resultNativeClient = await controllerB.RefreshToken(null, "native", default);

        string bodyNoClient = System.Text.Json.JsonSerializer.Serialize(
            resultNoClient.Should().BeOfType<OkObjectResult>().Subject.Value
        );
        string bodyNativeClient = System.Text.Json.JsonSerializer.Serialize(
            resultNativeClient.Should().BeOfType<OkObjectResult>().Subject.Value
        );

        // Both omit the refresh token from the body — custody was decided by the cookie, not by ?client=.
        bodyNoClient.Should().NotContain("new-ref-tok");
        bodyNativeClient.Should().NotContain("new-ref-tok");
    }

    // ─── scaffolding ───────────────────────────────────────────────────────────

    private static AuthResultDto Auth(string access, string refresh) =>
        new(
            access,
            refresh,
            ExpiresAt,
            new(
                Guid.NewGuid().ToString(),
                "stoney",
                "Stoney",
                ProfileImageUrl: null,
                Email: null,
                CreatedAt: DateTime.UtcNow,
                LastLoginAt: DateTime.UtcNow
            )
        );

    private static (AuthController Controller, IAuthService Auth) Build(
        string? allowedOrigin = null
    )
    {
        IUserService userService = Substitute.For<IUserService>();
        IAuthService authService = Substitute.For<IAuthService>();

        Dictionary<string, string?> settings = new() { ["App:BaseUrl"] = "http://localhost:5080" };
        if (allowedOrigin is not null)
            settings["Cors:Origins:0"] = allowedOrigin;
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        DefaultHttpContext http = new();
        http.Request.Scheme = "https";
        http.Request.Host = new("dash.example.test");

        AuthController controller = new(
            userService,
            authService,
            config,
            TimeProvider.System,
            Substitute.For<ITwitchOAuthStateService>(),
            Substitute.For<ILoginProviderRegistry>(),
            Substitute.For<IUserIdentityService>(),
            Array.Empty<ILoginIdentityProvider>(),
            Array.Empty<IAuthCodeLoginProvider>(),
            Substitute.For<IExternalLoginService>(),
            Substitute.For<ISessionService>()
        )
        {
            ControllerContext = new() { HttpContext = http },
        };
        return (controller, authService);
    }
}

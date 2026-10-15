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
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Platform.Caching;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// A web sign-in returns to the site it started from. The dashboard can be served from a second trusted origin
/// (the Vue dev site) that proxies <c>/api</c> to the bot, while the provider callback stays on the public base
/// URL. The start request's origin is kept only when it is trusted (Cors:Origins); the callback then hands the
/// session to that origin through a short-lived, single-use login code, never a token in a cross-origin URL.
/// The exchange sets the refresh cookie on the calling origin and works once, from that origin only.
/// </summary>
public sealed class AuthControllerWebReturnOriginTests
{
    private const string BaseOrigin = "https://dev.nomnomz.bot";
    private const string VueOrigin = "https://vue.nomnomz.bot";

    [Fact]
    public async Task TrustedForwardedOrigin_CallbackRedirectsThere_WithALoginCode_AndNoTokenInTheUrl()
    {
        Harness harness = new();

        string nonce = await harness.StartFromAsync("vue.nomnomz.bot", "/commands");
        RedirectResult redirect = await harness.CallbackAsync(nonce);

        redirect.Url.Should().StartWith($"{VueOrigin}/commands#login_code=");
        redirect.Url.Should().NotContain("access_token");
        redirect.Url.Should().NotContain("acc-tok");
        redirect.Url.Should().NotContain("ref-tok");
        // The session is not opened on the bot's own origin: no refresh cookie on the callback response.
        harness.LastCallbackSetCookie.Should().NotContain("nnz_refresh_token");
    }

    [Fact]
    public async Task ForgedUntrustedOrigin_IsDropped_AndTheRedirectStaysOnTheBaseUrl()
    {
        Harness harness = new();

        string nonce = await harness.StartFromAsync("evil.example", null);
        RedirectResult redirect = await harness.CallbackAsync(nonce);

        redirect.Url.Should().StartWith($"{BaseOrigin}/app#access_token=acc-tok");
        redirect.Url.Should().NotContain("evil.example");
        redirect.Url.Should().NotContain("login_code");
        harness.LastCallbackSetCookie.Should().Contain("nnz_refresh_token=ref-tok");
    }

    [Fact]
    public async Task Exchange_ConsumesTheCodeOnce_SetsTheRefreshCookie_AndReturnsTheAccessToken()
    {
        Harness harness = new();
        string nonce = await harness.StartFromAsync("vue.nomnomz.bot", null);
        string code = LoginCode(await harness.CallbackAsync(nonce));

        AuthController first = harness.Controller(VueOrigin);
        IActionResult result = await first.ExchangeWebLoginCode(new(code), default);

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        WebLoginExchangeDto data = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<WebLoginExchangeDto>>()
            .Subject.Data!;
        data.AccessToken.Should().Be("acc-tok");
        data.ExpiresIn.Should().BeGreaterThan(3000);
        data.User.Username.Should().Be("stoney");
        string setCookie = first.Response.Headers["Set-Cookie"].ToString().ToLowerInvariant();
        setCookie.Should().Contain("nnz_refresh_token=ref-tok");
        setCookie.Should().Contain("httponly");
        setCookie.Should().Contain("path=/api/v1/auth");

        AuthController second = harness.Controller(VueOrigin);
        IActionResult replay = await second.ExchangeWebLoginCode(new(code), default);

        StatusOf(replay).Should().Be(401);
        second.Response.Headers["Set-Cookie"].ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task Exchange_FromADifferentOrigin_Fails_AndBurnsTheCode()
    {
        Harness harness = new();
        string nonce = await harness.StartFromAsync("vue.nomnomz.bot", null);
        string code = LoginCode(await harness.CallbackAsync(nonce));

        AuthController attacker = harness.Controller("https://evil.example");
        IActionResult stolen = await attacker.ExchangeWebLoginCode(new(code), default);

        StatusOf(stolen).Should().Be(401);
        attacker.Response.Headers["Set-Cookie"].ToString().Should().BeEmpty();

        AuthController owner = harness.Controller(VueOrigin);
        IActionResult late = await owner.ExchangeWebLoginCode(new(code), default);
        StatusOf(late).Should().Be(401);
    }

    // ─── scaffolding ───────────────────────────────────────────────────────────

    private static string LoginCode(RedirectResult redirect)
    {
        string fragment = redirect.Url[(redirect.Url.IndexOf('#') + 1)..];
        string pair = fragment.Split('&').Single(p => p.StartsWith("login_code="));
        return pair["login_code=".Length..];
    }

    private static int? StatusOf(IActionResult result) =>
        (result as IStatusCodeActionResult)?.StatusCode;

    private sealed class Harness
    {
        private readonly IAuthService _auth = Substitute.For<IAuthService>();
        private readonly TwitchOAuthStateService _state = new(
            new MemoryCacheService(
                new MemoryCache(new MemoryCacheOptions()),
                NullLogger<MemoryCacheService>.Instance
            )
        );
        private readonly IConfiguration _config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["App:BaseUrl"] = BaseOrigin,
                    ["Cors:Origins:0"] = VueOrigin,
                }
            )
            .Build();

        public string LastCallbackSetCookie { get; private set; } = string.Empty;

        public Harness()
        {
            _auth
                .HandleTwitchCallbackAsync(
                    Arg.Any<OAuthCallbackDto>(),
                    Arg.Any<AuthContextDto>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(Result.Success(AuthResult()));
        }

        public async Task<string> StartFromAsync(string forwardedHost, string? returnTo)
        {
            string issued = string.Empty;
            _auth
                .GetTwitchOAuthUrl(
                    Arg.Do<string>(s => issued = s),
                    Arg.Any<string>(),
                    Arg.Any<Guid?>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(Result.Success("https://id.twitch.tv/oauth2/authorize?x=1"));

            AuthController controller = Controller(origin: null);
            controller.Request.Host = new(forwardedHost);
            controller.Request.Headers["X-Forwarded-Host"] = forwardedHost;
            controller.Request.Headers["X-Forwarded-Proto"] = "https";

            await controller.StartTwitchOAuth(null, "web", returnTo, null, null, default);
            return issued;
        }

        public async Task<RedirectResult> CallbackAsync(string nonce)
        {
            AuthController controller = Controller(origin: null);
            IActionResult result = await controller.HandleTwitchCallback("code", nonce, default);
            LastCallbackSetCookie = controller.Response.Headers["Set-Cookie"].ToString();
            return result.Should().BeOfType<RedirectResult>().Subject;
        }

        public AuthController Controller(string? origin)
        {
            DefaultHttpContext http = new();
            http.Request.Scheme = "http";
            http.Request.Host = new("dev.nomnomz.bot");
            if (origin is not null)
                http.Request.Headers.Origin = origin;

            return new(
                Substitute.For<IUserService>(),
                _auth,
                _config,
                TestHostEnvironment.Production,
                TimeProvider.System,
                _state,
                Substitute.For<ILoginProviderRegistry>(),
                Substitute.For<IUserIdentityService>(),
                [],
                [],
                Substitute.For<IExternalLoginService>(),
                Substitute.For<ISessionService>(),
                Substitute.For<ISystemCredentialsProvider>(),
                Substitute.For<IImpersonationSessionService>()
            )
            {
                ControllerContext = new() { HttpContext = http },
            };
        }

        private static AuthResultDto AuthResult() =>
            new(
                "acc-tok",
                "ref-tok",
                DateTime.UtcNow.AddHours(1),
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
    }
}

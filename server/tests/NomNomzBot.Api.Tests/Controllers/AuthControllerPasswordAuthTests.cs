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
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// <c>POST auth/register</c> / <c>POST auth/login</c> (the generic email+password login screen's backing
/// endpoints): the web client custody rule — refresh token in the HttpOnly cookie, never the JSON body — same
/// as every other login path; native keeps it in the body.
/// </summary>
public sealed class AuthControllerPasswordAuthTests
{
    private static AuthController Build(
        IPasswordAuthService passwordAuth,
        DefaultHttpContext http
    ) =>
        new(
            Substitute.For<IUserService>(),
            Substitute.For<IAuthService>(),
            new ConfigurationBuilder().Build(),
            TestHostEnvironment.Development,
            TimeProvider.System,
            Substitute.For<ITwitchOAuthStateService>(),
            Substitute.For<ILoginProviderRegistry>(),
            Substitute.For<IUserIdentityService>(),
            [],
            [],
            Substitute.For<IExternalLoginService>(),
            Substitute.For<ISessionService>(),
            Substitute.For<ISystemCredentialsProvider>(),
            passwordAuth
        )
        {
            ControllerContext = new() { HttpContext = http },
        };

    private static AuthResultDto SampleAuth() =>
        new(
            "access-tok",
            "refresh-tok",
            DateTime.UtcNow.AddHours(1),
            new UserDto("u-1", "streamer", "Streamer", null, null, DateTime.UtcNow, DateTime.UtcNow)
        );

    [Fact]
    public async Task Register_for_the_web_client_sets_the_refresh_cookie_and_blanks_the_body_token()
    {
        IPasswordAuthService passwordAuth = Substitute.For<IPasswordAuthService>();
        passwordAuth
            .RegisterAsync(
                "new@example.com",
                "correct horse battery",
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(SampleAuth()));

        DefaultHttpContext http = new();
        AuthController controller = Build(passwordAuth, http);

        IActionResult result = await controller.Register(
            new("new@example.com", "correct horse battery"),
            client: "web",
            default
        );

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<AuthResultDto> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<AuthResultDto>>()
            .Subject;
        body.Data!.RefreshToken.Should().BeEmpty();
        body.Data.AccessToken.Should().Be("access-tok");

        http.Response.Headers.SetCookie.ToString()
            .Should()
            .Contain("nnz_refresh_token=refresh-tok");
    }

    [Fact]
    public async Task Register_for_a_native_client_keeps_the_refresh_token_in_the_body_and_sets_no_cookie()
    {
        IPasswordAuthService passwordAuth = Substitute.For<IPasswordAuthService>();
        passwordAuth
            .RegisterAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(SampleAuth()));

        DefaultHttpContext http = new();
        AuthController controller = Build(passwordAuth, http);

        IActionResult result = await controller.Register(
            new("new@example.com", "correct horse battery"),
            client: "desktop",
            default
        );

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<AuthResultDto> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<AuthResultDto>>()
            .Subject;
        body.Data!.RefreshToken.Should().Be("refresh-tok");
        http.Response.Headers.SetCookie.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task Login_with_a_failing_result_returns_the_error_envelope_and_sets_no_cookie()
    {
        IPasswordAuthService passwordAuth = Substitute.For<IPasswordAuthService>();
        passwordAuth
            .LoginAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<AuthResultDto>("Invalid email or password.", "INVALID_CREDENTIALS")
            );

        DefaultHttpContext http = new();
        AuthController controller = Build(passwordAuth, http);

        IActionResult result = await controller.Login(
            new("nobody@example.com", "whatever"),
            client: "web",
            default
        );

        result.Should().NotBeOfType<OkObjectResult>();
        http.Response.Headers.SetCookie.ToString().Should().BeEmpty();
    }
}

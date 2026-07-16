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
using Xunit;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The generic <c>auth/{provider}/device/poll</c> route (platform-identity §5) dispatches an enabled non-Twitch
/// provider through its <see cref="ILoginIdentityProvider"/>: an approved poll runs the generic login into a
/// session; a pending poll surfaces the loop status.
/// </summary>
public sealed class AuthControllerDeviceLoginTests
{
    private static AuthController Build(ILoginIdentityProvider impl, IExternalLoginService ext)
    {
        ILoginProviderRegistry registry = Substitute.For<ILoginProviderRegistry>();
        LoginProviderDescriptor yt = new(
            "youtube",
            "YouTube",
            LoginFlows.DeviceCode,
            "use_youtube_login",
            ["openid"]
        );
        registry.Get("youtube").Returns(Result.Success(yt));
        registry
            .EnabledAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<LoginProviderDescriptor>>([yt]));

        return new AuthController(
            Substitute.For<IUserService>(),
            Substitute.For<IAuthService>(),
            new ConfigurationBuilder().Build(),
            TimeProvider.System,
            Substitute.For<ITwitchOAuthStateService>(),
            registry,
            Substitute.For<IUserIdentityService>(),
            [impl],
            Array.Empty<IAuthCodeLoginProvider>(),
            ext,
            Substitute.For<ISessionService>()
        )
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    [Fact]
    public async Task PollDeviceLogin_youtube_authorized_establishes_a_session()
    {
        ILoginIdentityProvider impl = Substitute.For<ILoginIdentityProvider>();
        impl.Key.Returns("youtube");
        impl.PollDeviceAsync("dc", Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new ExternalIdentityProof(
                        "youtube",
                        "yt-1",
                        "creator",
                        "Creator",
                        null,
                        Guid.CreateVersion7()
                    )
                )
            );

        IExternalLoginService ext = Substitute.For<IExternalLoginService>();
        AuthResultDto auth = new(
            "acc",
            "ref",
            DateTime.UtcNow.AddHours(1),
            new UserDto("id", "creator", "Creator", null, null, DateTime.UtcNow, DateTime.UtcNow)
        );
        ext.LoginAsync(
                Arg.Any<ExternalIdentityProof>(),
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(auth));

        IActionResult result = await Build(impl, ext)
            .PollDeviceLogin("youtube", new DevicePollRequest("dc"), null, default);

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<DeviceLoginPollDto> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<DeviceLoginPollDto>>()
            .Subject;
        body.Data!.Status.Should().Be(DeviceLoginStatus.Authorized);
        body.Data.Auth!.AccessToken.Should().Be("acc");
    }

    [Fact]
    public async Task PollDeviceLogin_youtube_pending_returns_the_poll_status()
    {
        ILoginIdentityProvider impl = Substitute.For<ILoginIdentityProvider>();
        impl.Key.Returns("youtube");
        impl.PollDeviceAsync("dc", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ExternalIdentityProof>("pending", DeviceLoginStatus.Pending));

        IActionResult result = await Build(impl, Substitute.For<IExternalLoginService>())
            .PollDeviceLogin("youtube", new DevicePollRequest("dc"), null, default);

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<DeviceLoginPollDto> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<DeviceLoginPollDto>>()
            .Subject;
        body.Data!.Status.Should().Be(DeviceLoginStatus.Pending);
        body.Data.Auth.Should().BeNull();
    }
}

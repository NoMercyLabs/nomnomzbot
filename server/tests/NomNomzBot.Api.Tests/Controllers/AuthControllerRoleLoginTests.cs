// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Platform.Caching;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Only a broadcaster sign-in opens a channel. A viewer or moderator who signs in through the role choice runs
/// the identity-only callback, so the bot never creates a channel row for them.
/// </summary>
public sealed class AuthControllerRoleLoginTests
{
    [Theory]
    [InlineData("viewer")]
    [InlineData("moderator")]
    public async Task Non_broadcaster_sign_in_never_runs_the_channel_creating_callback(string role)
    {
        IAuthService auth = await SignInAsync(role);

        await auth.DidNotReceive()
            .HandleTwitchCallbackAsync(
                Arg.Any<OAuthCallbackDto>(),
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>()
            );
        await auth.Received(1)
            .HandleTwitchViewerCallbackAsync(
                Arg.Any<OAuthCallbackDto>(),
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Guid?>(),
                Arg.Any<Guid?>()
            );
    }

    [Fact]
    public async Task Broadcaster_sign_in_runs_the_channel_callback()
    {
        IAuthService auth = await SignInAsync("broadcaster");

        await auth.Received(1)
            .HandleTwitchCallbackAsync(
                Arg.Any<OAuthCallbackDto>(),
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>()
            );
        await auth.DidNotReceive()
            .HandleTwitchViewerCallbackAsync(
                Arg.Any<OAuthCallbackDto>(),
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Guid?>(),
                Arg.Any<Guid?>()
            );
    }

    private static async Task<IAuthService> SignInAsync(string role)
    {
        IAuthService auth = Substitute.For<IAuthService>();
        TwitchOAuthStateService state = new(
            new MemoryCacheService(
                new MemoryCache(new MemoryCacheOptions()),
                NullLogger<MemoryCacheService>.Instance
            )
        );
        string issued = string.Empty;
        auth.GetTwitchOAuthUrl(
                Arg.Any<TwitchLoginRole>(),
                Arg.Do<string>(s => issued = s),
                Arg.Any<string>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Guid?>()
            )
            .Returns(Result.Success("https://id.twitch.tv/oauth2/authorize?x=1"));
        auth.HandleTwitchCallbackAsync(
                Arg.Any<OAuthCallbackDto>(),
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<AuthResultDto>("stop", "STOP"));
        auth.HandleTwitchViewerCallbackAsync(
                Arg.Any<OAuthCallbackDto>(),
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Guid?>(),
                Arg.Any<Guid?>()
            )
            .Returns(Result.Failure<AuthResultDto>("stop", "STOP"));

        await Controller(auth, state).StartTwitchOAuth(null, null, null, null, role);
        await Controller(auth, state)
            .HandleTwitchCallback("code", issued, Substitute.For<IInviteCodeService>(), default);
        return auth;
    }

    private static AuthController Controller(IAuthService auth, ITwitchOAuthStateService state)
    {
        DefaultHttpContext http = new();
        http.Request.Scheme = "https";
        http.Request.Host = new("dev.nomnomz.bot");
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["App:BaseUrl"] = "https://dev.nomnomz.bot" }
            )
            .Build();

        return new(
            Substitute.For<IUserService>(),
            auth,
            config,
            TestHostEnvironment.Production,
            TimeProvider.System,
            state,
            Substitute.For<ILoginProviderRegistry>(),
            Substitute.For<IUserIdentityService>(),
            [],
            [],
            Substitute.For<IExternalLoginService>(),
            Substitute.For<ISessionService>(),
            Substitute.For<ISystemCredentialsProvider>(),
            Substitute.For<IImpersonationSessionService>(),
            Substitute.For<Application.Abstractions.Auth.IViewAsScope>()
        )
        {
            ControllerContext = new() { HttpContext = http },
        };
    }
}

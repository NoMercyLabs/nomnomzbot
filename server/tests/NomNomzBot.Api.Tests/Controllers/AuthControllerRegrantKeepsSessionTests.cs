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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Platform.Caching;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// A moderator or viewer who signs in again from a live web session: the start reads that session from the
/// HttpOnly refresh cookie, the person and the channel travel in the server-side flow state, and the callback
/// hands both to the identity-only sign-in so the new session lands on the same channel.
/// </summary>
public sealed class AuthControllerRegrantKeepsSessionTests
{
    private static readonly Guid Person = Guid.Parse("0192a000-0000-7000-8000-0000000000a1");
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000c1");

    [Theory]
    [InlineData("viewer")]
    [InlineData("moderator")]
    public async Task Start_names_the_signed_in_person_to_the_authorize_url_and_the_callback_gets_person_and_channel(
        string role
    )
    {
        IAuthService auth = Substitute.For<IAuthService>();
        Guid? userHint = null;
        Guid? arrivingUser = null;
        Guid? arrivingChannel = null;
        string issued = string.Empty;
        auth.GetTwitchOAuthUrl(
                Arg.Any<TwitchLoginRole>(),
                Arg.Do<string>(s => issued = s),
                Arg.Any<string>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>(),
                Arg.Do<Guid?>(hint => userHint = hint)
            )
            .Returns(Result.Success("https://id.twitch.tv/oauth2/authorize?x=1"));
        auth.HandleTwitchViewerCallbackAsync(
                Arg.Any<OAuthCallbackDto>(),
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>(),
                Arg.Do<Guid?>(user => arrivingUser = user),
                Arg.Do<Guid?>(channel => arrivingChannel = channel)
            )
            .Returns(Result.Failure<AuthResultDto>("stop", "STOP"));
        ISessionService sessions = Substitute.For<ISessionService>();
        sessions
            .PeekSessionAsync("raw-refresh", Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new AuthSessionDto(
                        Guid.NewGuid(),
                        Person,
                        Channel,
                        "web",
                        DateTime.UtcNow,
                        DateTime.UtcNow.AddDays(30),
                        false
                    )
                )
            );
        TwitchOAuthStateService state = new(
            new MemoryCacheService(
                new MemoryCache(new MemoryCacheOptions()),
                NullLogger<MemoryCacheService>.Instance
            )
        );

        await Controller(auth, state, sessions, "raw-refresh")
            .StartTwitchOAuth(null, null, null, null, role, default);
        await Controller(auth, state, sessions, null).HandleTwitchCallback("code", issued, default);

        userHint.Should().Be(Person);
        arrivingUser.Should().Be(Person);
        arrivingChannel.Should().Be(Channel);
    }

    [Fact]
    public async Task Start_without_a_session_cookie_names_nobody()
    {
        IAuthService auth = Substitute.For<IAuthService>();
        Guid? userHint = Guid.NewGuid();
        Guid? arrivingUser = Guid.NewGuid();
        string issued = string.Empty;
        auth.GetTwitchOAuthUrl(
                Arg.Any<TwitchLoginRole>(),
                Arg.Do<string>(s => issued = s),
                Arg.Any<string>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>(),
                Arg.Do<Guid?>(hint => userHint = hint)
            )
            .Returns(Result.Success("https://id.twitch.tv/oauth2/authorize?x=1"));
        auth.HandleTwitchViewerCallbackAsync(
                Arg.Any<OAuthCallbackDto>(),
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>(),
                Arg.Do<Guid?>(user => arrivingUser = user),
                Arg.Any<Guid?>()
            )
            .Returns(Result.Failure<AuthResultDto>("stop", "STOP"));
        TwitchOAuthStateService state = new(
            new MemoryCacheService(
                new MemoryCache(new MemoryCacheOptions()),
                NullLogger<MemoryCacheService>.Instance
            )
        );
        ISessionService sessions = Substitute.For<ISessionService>();

        await Controller(auth, state, sessions, null)
            .StartTwitchOAuth(null, null, null, null, "moderator", default);
        await Controller(auth, state, sessions, null).HandleTwitchCallback("code", issued, default);

        userHint.Should().BeNull();
        arrivingUser.Should().BeNull();
    }

    private static AuthController Controller(
        IAuthService auth,
        ITwitchOAuthStateService state,
        ISessionService sessions,
        string? refreshCookie
    )
    {
        DefaultHttpContext http = new();
        http.Request.Scheme = "https";
        http.Request.Host = new("dev.nomnomz.bot");
        if (refreshCookie is not null)
            http.Request.Headers.Cookie = $"nnz_refresh_token={refreshCookie}";
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
            sessions,
            Substitute.For<ISystemCredentialsProvider>(),
            Substitute.For<IImpersonationSessionService>(),
            Substitute.For<Application.Abstractions.Auth.IViewAsScope>()
        )
        {
            ControllerContext = new() { HttpContext = http },
        };
    }
}

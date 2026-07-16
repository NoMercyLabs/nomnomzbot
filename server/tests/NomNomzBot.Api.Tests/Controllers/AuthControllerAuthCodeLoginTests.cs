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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NSubstitute;
using Xunit;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The generic <c>auth/{provider}/authorize</c> route (platform-identity §10.3) dispatches an enabled
/// auth-code + PKCE provider (kick / twitter): it stashes the PKCE verifier + provider key server-side and
/// 302s to the provider's authorize URL.
/// </summary>
public sealed class AuthControllerAuthCodeLoginTests
{
    private static AuthController Build(IAuthCodeLoginProvider impl, ITwitchOAuthStateService state)
    {
        ILoginProviderRegistry registry = Substitute.For<ILoginProviderRegistry>();
        LoginProviderDescriptor kick = new(
            "kick",
            "Kick",
            LoginFlows.AuthCodePkce,
            "use_kick_login",
            ["user:read"]
        );
        registry.Get("kick").Returns(Result.Success(kick));
        registry
            .EnabledAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<LoginProviderDescriptor>>([kick]));

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["App:BaseUrl"] = "http://localhost:5080" }
            )
            .Build();

        DefaultHttpContext http = new();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("bot.test");

        return new AuthController(
            Substitute.For<IUserService>(),
            Substitute.For<IAuthService>(),
            config,
            TimeProvider.System,
            state,
            registry,
            Substitute.For<IUserIdentityService>(),
            Array.Empty<ILoginIdentityProvider>(),
            [impl],
            Substitute.For<IExternalLoginService>(),
            Substitute.For<ISessionService>()
        )
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    [Fact]
    public async Task StartExternalAuthorize_kick_redirects_and_stashes_the_pkce_verifier()
    {
        IAuthCodeLoginProvider impl = Substitute.For<IAuthCodeLoginProvider>();
        impl.Key.Returns("kick");
        impl.BuildAuthorizeUrlAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new Uri("https://id.kick.com/oauth/authorize?x=1")));

        ITwitchOAuthStateService state = Substitute.For<ITwitchOAuthStateService>();
        state
            .IssueAsync(Arg.Any<TwitchOAuthFlowState>(), Arg.Any<CancellationToken>())
            .Returns("nonce");

        IActionResult result = await Build(impl, state)
            .StartExternalAuthorize("kick", redirect_uri: null, client: "web", default);

        RedirectResult redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().Be("https://id.kick.com/oauth/authorize?x=1");

        // The PKCE verifier + provider key were persisted server-side under the state nonce.
        await state
            .Received(1)
            .IssueAsync(
                Arg.Is<TwitchOAuthFlowState>(s =>
                    s.Provider == "kick"
                    && !string.IsNullOrEmpty(s.CodeVerifier)
                    && s.Flow == "login"
                ),
                Arg.Any<CancellationToken>()
            );
    }
}

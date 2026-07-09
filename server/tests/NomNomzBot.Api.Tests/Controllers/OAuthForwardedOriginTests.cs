// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Application.Identity.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the redirect_uri bug fix end-to-end at the controller seam (deployment-distribution §6): when the
/// dashboard is reached through a Cloudflare tunnel (<c>X-Forwarded-Host: bot-dev.nomercy.tv</c> +
/// <c>X-Forwarded-Proto: https</c>) over a loopback-bound listener, every OAuth flow builds its redirect_uri
/// from the <b>tunnel origin</b>, never the localhost the listener happens to bind — so it matches the URL the
/// credential card tells the owner to register. The auto-set loopback <c>App:BaseUrl</c> must not override the
/// forwarded host. Spotify is covered at the service seam (IntegrationOAuthServiceTests); Twitch + Discord here.
/// </summary>
public sealed class OAuthForwardedOriginTests
{
    private const string TunnelOrigin = "https://bot-dev.nomercy.tv";
    private const string ChannelId = "0192a000-0000-7000-8000-00000000d1c0";

    [Fact]
    public async Task TwitchLogin_BehindTunnel_PassesTunnelOriginToOAuthUrlBuilder()
    {
        IAuthService auth = Substitute.For<IAuthService>();
        ITwitchOAuthStateService state = Substitute.For<ITwitchOAuthStateService>();
        state
            .IssueAsync(Arg.Any<TwitchOAuthFlowState>(), Arg.Any<CancellationToken>())
            .Returns("nonce");
        auth.GetTwitchOAuthUrl("nonce", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success("https://id.twitch.tv/oauth2/authorize?x=1"));

        AuthController controller = new(
            Substitute.For<IUserService>(),
            auth,
            LoopbackConfig(),
            TimeProvider.System,
            state,
            Substitute.For<ILoginProviderRegistry>(),
            Substitute.For<IUserIdentityService>(),
            Array.Empty<ILoginIdentityProvider>(),
            Substitute.For<IExternalLoginService>()
        )
        {
            ControllerContext = ForwardedContext(),
        };

        await controller.StartTwitchOAuth(redirect_uri: null, client: "web", default);

        // The base URL the OAuth URL is built from is the tunnel origin — so the resulting Twitch
        // redirect_uri (…/api/v1/auth/twitch/callback) is the domain, exactly what the owner registers.
        await auth.Received(1)
            .GetTwitchOAuthUrl("nonce", TunnelOrigin, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DiscordStart_BehindTunnel_RedirectsWithTunnelRedirectUri()
    {
        (DiscordOAuthController controller, IDiscordOAuthStateService _) = BuildDiscord();

        IActionResult result = await controller.StartDiscordOAuth(
            ChannelId,
            redirect_uri: null,
            default
        );

        RedirectResult redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().StartWith("https://discord.com/api/oauth2/authorize");
        // The bot install requests the guild-install context, so Discord doesn't reject it with
        // "installation type not supported for this application."
        redirect.Url.Should().Contain("integration_type=0");
        // The redirect_uri Discord is told to call back is the tunnel origin, never localhost.
        Uri.UnescapeDataString(redirect.Url)
            .Should()
            .Contain($"redirect_uri={TunnelOrigin}/api/v1/integrations/discord/callback");
        Uri.UnescapeDataString(redirect.Url).Should().NotContain("localhost");
    }

    // ─── scaffolding ───────────────────────────────────────────────────────────

    /// <summary>Config where App:BaseUrl is the auto-set loopback default — the value ListenPortBootstrap writes.</summary>
    private static IConfiguration LoopbackConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["App:BaseUrl"] = "http://localhost:5080" }
            )
            .Build();

    /// <summary>A request that arrived through the tunnel: loopback-bound listener, but the proxy forwarded the public host + https.</summary>
    private static ControllerContext ForwardedContext()
    {
        DefaultHttpContext http = new();
        http.Request.Scheme = "http";
        http.Request.Host = new HostString("localhost", 5080);
        http.Request.Headers["X-Forwarded-Host"] = "bot-dev.nomercy.tv";
        http.Request.Headers["X-Forwarded-Proto"] = "https";
        return new ControllerContext { HttpContext = http };
    }

    private static (
        DiscordOAuthController Controller,
        IDiscordOAuthStateService State
    ) BuildDiscord()
    {
        ApiTestDbContext db = ApiTestDbContext.New();
        db.Configurations.Add(
            new NomNomzBot.Domain.Platform.Entities.Configuration
            {
                BroadcasterId = null,
                Key = "discord.client_id",
                Value = "discord-client",
            }
        );
        db.SaveChanges();

        IDiscordOAuthStateService state = Substitute.For<IDiscordOAuthStateService>();
        state
            .IssueAsync(Arg.Any<DiscordOAuthFlowState>(), Arg.Any<CancellationToken>())
            .Returns("nonce");

        DiscordOAuthController controller = new(
            db,
            LoopbackConfig(),
            new SingleClientFactory(new OkHandler()),
            NullLogger<DiscordOAuthController>.Instance,
            TimeProvider.System,
            state,
            Substitute.For<IDiscordGuildService>()
        )
        {
            ControllerContext = ForwardedContext(),
        };
        return (controller, state);
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                }
            );
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}

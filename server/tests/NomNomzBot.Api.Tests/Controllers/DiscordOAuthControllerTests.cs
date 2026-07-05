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
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the Discord bot-install OAuth flow's desktop-loopback completion (KMP onboarding gap 1):
/// <list type="bullet">
///   <item><b>start</b> rejects a non-loopback <c>redirect_uri</c> (400, the open-redirect boundary) and
///     accepts a loopback one (redirects to Discord, carrying a server-side state nonce);</item>
///   <item>the <b>callback</b>, when a loopback <c>redirect_uri</c> was supplied, bounces back to that listener
///     with <c>discord_connected=true</c> on success / <c>error=&lt;reason&gt;</c> on failure;</item>
///   <item>and falls back to the web <c>FrontendUrl</c> redirect when none was supplied.</item>
/// </list>
/// </summary>
public sealed class DiscordOAuthControllerTests
{
    private const string ChannelId = "0192a000-0000-7000-8000-00000000d1c0";
    private const string Loopback = "http://127.0.0.1:53127/callback";

    // ─── start: the open-redirect boundary ─────────────────────────────────────

    [Fact]
    public async Task Start_NonLoopbackRedirectUri_Returns400()
    {
        (DiscordOAuthController controller, _, _) = Build();

        IActionResult result = await controller.StartDiscordOAuth(
            ChannelId,
            redirect_uri: "https://evil.example/steal",
            default
        );

        // Rejected by ClientRedirectPolicy BEFORE any provider redirect is issued.
        result.Should().BeAssignableTo<ObjectResult>().Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Start_LoopbackRedirectUri_RedirectsToDiscordWithState()
    {
        (DiscordOAuthController controller, _, IDiscordOAuthStateService state) = Build();

        IActionResult result = await controller.StartDiscordOAuth(ChannelId, Loopback, default);

        RedirectResult redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().StartWith("https://discord.com/api/oauth2/authorize");
        redirect.Url.Should().Contain("state=");

        // The loopback redirect was carried server-side in the issued Discord flow state (never in the query).
        await state
            .Received(1)
            .IssueAsync(
                Arg.Is<DiscordOAuthFlowState>(s =>
                    s.ChannelId == ChannelId && s.RedirectUri == Loopback
                ),
                Arg.Any<CancellationToken>()
            );
    }

    // ─── callback: loopback completion (success / failure) + web fallback ───────

    [Fact]
    public async Task Callback_WithLoopback_Success_RedirectsToLoopbackWithConnectedMarker()
    {
        (DiscordOAuthController controller, FakeDiscordGuildService discord, _) = Build(
            new StubHandler
            {
                TokenJson =
                    """{"access_token":"bot-token","refresh_token":"r","expires_in":604800,"guild":{"id":"99","name":"Cool Guild"}}""",
            },
            new DiscordOAuthFlowState(ChannelId, Loopback)
        );

        IActionResult result = await controller.HandleDiscordCallback("the-code", "nonce", default);

        RedirectResult redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().Be($"{Loopback}?discord_connected=true");

        // The guild link was actually recorded through the guild service (the consequence of the action).
        discord.UpsertCalls.Should().ContainSingle();
        discord.UpsertCalls[0].GuildId.Should().Be("99");
    }

    [Fact]
    public async Task Callback_WithLoopback_TokenExchangeFails_RedirectsToLoopbackWithError()
    {
        (DiscordOAuthController controller, FakeDiscordGuildService discord, _) = Build(
            new StubHandler { TokenStatus = HttpStatusCode.BadRequest },
            new DiscordOAuthFlowState(ChannelId, Loopback)
        );

        IActionResult result = await controller.HandleDiscordCallback("bad-code", "nonce", default);

        RedirectResult redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().Be($"{Loopback}?error=token_exchange_failed");

        // A failed exchange never persists a guild link.
        discord.UpsertCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Callback_NoLoopback_Success_RedirectsToOAuthRelay()
    {
        (DiscordOAuthController controller, _, _) = Build(
            new StubHandler
            {
                TokenJson =
                    """{"access_token":"bot-token","refresh_token":"r","expires_in":604800,"guild":{"id":"99","name":"Cool Guild"}}""",
            },
            new DiscordOAuthFlowState(ChannelId, RedirectUri: null)
        );

        IActionResult result = await controller.HandleDiscordCallback("the-code", "nonce", default);

        // The web fallback must land on the /oauth-relay seam (popup postMessage + close, or full-page
        // fallback to the app root) — NOT a hand-rolled frontend deep link. The old target embedded the
        // "(dashboard)" route-GROUP folder literal, which the hash-routed Wasm app cannot match → black screen.
        // The relay is a BACKEND-served page (/oauth-relay), so it lands on the public API/access origin
        // (App:BaseUrl here), NOT the separate App:FrontendUrl — which doesn't serve /oauth-relay at all.
        RedirectResult redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().StartWith("https://api.example.test/oauth-relay");
        redirect.Url.Should().Contain("discord_connected=true");
        redirect.Url.Should().NotContain("(dashboard)");
        redirect.Url.Should().NotContain("127.0.0.1");
    }

    // ─── scaffolding ───────────────────────────────────────────────────────────

    private static (
        DiscordOAuthController Controller,
        FakeDiscordGuildService Discord,
        IDiscordOAuthStateService State
    ) Build(StubHandler? handler = null, DiscordOAuthFlowState? consumeState = null)
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
        db.Configurations.Add(
            new NomNomzBot.Domain.Platform.Entities.Configuration
            {
                BroadcasterId = null,
                Key = "discord.client_secret",
                SecureValue = "discord-secret",
            }
        );
        db.SaveChanges();

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["App:BaseUrl"] = "https://api.example.test",
                    ["App:FrontendUrl"] = "https://web.example",
                }
            )
            .Build();

        IDiscordOAuthStateService state = Substitute.For<IDiscordOAuthStateService>();
        state
            .IssueAsync(Arg.Any<DiscordOAuthFlowState>(), Arg.Any<CancellationToken>())
            .Returns("nonce");
        state.ConsumeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(consumeState);

        FakeDiscordGuildService discord = new();

        DiscordOAuthController controller = new(
            db,
            config,
            new SingleClientFactory(handler ?? new StubHandler()),
            NullLogger<DiscordOAuthController>.Instance,
            TimeProvider.System,
            state,
            discord
        )
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return (controller, discord, state);
    }

    /// <summary>A canned Discord token endpoint: returns <see cref="TokenJson"/> at <see cref="TokenStatus"/>.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public string TokenJson { get; init; } =
            """{"access_token":"bot-token","expires_in":604800,"guild":{"id":"99","name":"Cool Guild"}}""";
        public HttpStatusCode TokenStatus { get; init; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(TokenStatus)
                {
                    Content = new StringContent(TokenJson, Encoding.UTF8, "application/json"),
                }
            );
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>
    /// A guild-service double at the <see cref="IDiscordGuildService"/> seam that records the OAuth upsert so a
    /// test can assert the link was (or was not) recorded — the actual consequence of a successful callback.
    /// </summary>
    private sealed class FakeDiscordGuildService : IDiscordGuildService
    {
        public List<DiscordGuildOAuthResult> UpsertCalls { get; } = [];

        public Task<Result<DiscordGuildConnectionDto>> UpsertFromOAuthAsync(
            Guid broadcasterId,
            DiscordGuildOAuthResult oauth,
            CancellationToken ct = default
        )
        {
            UpsertCalls.Add(oauth);
            return Task.FromResult(
                Result.Success(
                    new DiscordGuildConnectionDto(
                        Guid.NewGuid(),
                        broadcasterId,
                        oauth.GuildId,
                        oauth.GuildName,
                        BotInstalled: true,
                        ServerConsentStatus: "approved",
                        ApprovedByDiscordUserId: null,
                        ApprovedAt: null,
                        StreamerEnabled: true,
                        IsLinkActive: true,
                        CreatedAt: DateTime.UtcNow,
                        UpdatedAt: DateTime.UtcNow
                    )
                )
            );
        }

        public Task<Result<IReadOnlyList<DiscordGuildConnectionDto>>> GetConnectionsAsync(
            Guid broadcasterId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result<DiscordGuildConnectionDto>> GetConnectionAsync(
            Guid broadcasterId,
            Guid connectionId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result> ApproveServerConsentAsync(
            Guid broadcasterId,
            Guid connectionId,
            string approvedByDiscordUserId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result> RevokeServerConsentAsync(
            Guid broadcasterId,
            Guid connectionId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result> SetStreamerEnabledAsync(
            Guid broadcasterId,
            Guid connectionId,
            bool enabled,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result> DisconnectAsync(
            Guid broadcasterId,
            Guid connectionId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result<bool>> IsLinkActiveAsync(
            Guid broadcasterId,
            Guid connectionId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}

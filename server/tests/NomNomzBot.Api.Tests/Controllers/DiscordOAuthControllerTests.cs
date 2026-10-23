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
using NomNomzBot.Application.Common.Consequences;
using NomNomzBot.Application.Common.Interfaces;
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
    private const string OpenedSecret = "opened-discord-secret";

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

        // The bot asks for what it needs to post and repair its own channel access: View Channel, Send Messages,
        // Embed Links, Read History (0x4c00 + 0x10000) and Manage Roles (0x10000000) = 268,520,448.
        redirect.Url.Should().Contain("&permissions=268520448");

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

    // ─── callback: the app credentials sent to Discord ─────────────────────────

    // The admin screen seals the client secret; sending the stored (sealed) text made Discord answer
    // invalid_client for every secret a person could paste.
    [Fact]
    public async Task Callback_SendsTheOpenedClientSecretAndTheClientIdToDiscord()
    {
        StubHandler handler = new();
        (DiscordOAuthController controller, _, _) = Build(
            handler,
            new(ChannelId, RedirectUri: null)
        );

        await controller.HandleDiscordCallback("the-code", "nonce", null, default);

        Dictionary<string, string> form = handler
            .SentForm!.Split('&')
            .Select(pair => pair.Split('='))
            .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]));

        form["client_id"].Should().Be("discord-client");
        form["client_secret"].Should().Be(OpenedSecret);
        form["code"].Should().Be("the-code");
        form["grant_type"].Should().Be("authorization_code");
    }

    // The live role goes to the streamer's own Discord account. Discord itself knows which Twitch account is linked
    // to a profile, so the install reads that link instead of asking the streamer to pick themselves from a list.
    [Fact]
    public async Task Callback_ReadsTheInstallersDiscordIdAndTheirLinkedTwitchAccounts()
    {
        StubHandler handler = new()
        {
            ConnectionsJson = """[{"type":"youtube","id":"yt-1"},{"type":"twitch","id":"12345"}]""",
        };
        (DiscordOAuthController controller, FakeDiscordGuildService discord, _) = Build(
            handler,
            new(ChannelId, RedirectUri: null)
        );

        await controller.HandleDiscordCallback("the-code", "nonce", null, default);

        DiscordGuildOAuthResult recorded = discord.UpsertCalls.Should().ContainSingle().Subject;
        recorded.InstalledByDiscordUserId.Should().Be("installer-1");
        recorded.LinkedTwitchIds.Should().Equal("12345");
    }

    [Fact]
    public async Task Callback_WhenDiscordWithholdsTheLinks_StillAddsTheBotAndRecordsNoLinkedTwitch()
    {
        StubHandler handler = new() { ConnectionsStatus = HttpStatusCode.Forbidden };
        (DiscordOAuthController controller, FakeDiscordGuildService discord, _) = Build(
            handler,
            new(ChannelId, RedirectUri: null)
        );

        IActionResult result = await controller.HandleDiscordCallback(
            "the-code",
            "nonce",
            null,
            default
        );

        result.Should().BeOfType<RedirectResult>();
        discord.UpsertCalls.Should().ContainSingle().Which.LinkedTwitchIds.Should().BeEmpty();
    }

    // ─── callback: a failure in the browser lands on the dashboard with a reason, never as raw JSON ───

    // A failure goes through the relay page, so a popup tells the dashboard and closes; with no opener the relay
    // carries the person on to the Discord page, which says in words what went wrong.
    private static string Failed(string reason) =>
        $"https://api.example.test/oauth-relay?discord_error={reason}"
        + $"&return=%2Fapp%2Fsettings%2Fdiscord%3Fdiscord_error%3D{reason}";

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "token_exchange_failed")]
    [InlineData(HttpStatusCode.BadRequest, "token_exchange_failed")]
    public async Task Callback_Web_TokenExchangeFails_RedirectsToTheDiscordPageWithTheReason(
        HttpStatusCode status,
        string reason
    )
    {
        (DiscordOAuthController controller, FakeDiscordGuildService discord, _) = Build(
            new() { TokenStatus = status },
            new(ChannelId, RedirectUri: null)
        );

        IActionResult result = await controller.HandleDiscordCallback("c", "nonce", null, default);

        result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be(Failed(reason));
        discord.UpsertCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Callback_Web_ExpiredState_RedirectsToTheDiscordPageWithTheReason()
    {
        (DiscordOAuthController controller, _, _) = Build(consumeState: null);

        IActionResult result = await controller.HandleDiscordCallback("c", "gone", null, default);

        result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be(Failed("invalid_state"));
    }

    // Pressing Cancel on Discord's page sends back error=access_denied and no code at all.
    [Fact]
    public async Task Callback_Web_PersonCancelsOnDiscord_RedirectsWithAccessDenied()
    {
        (DiscordOAuthController controller, FakeDiscordGuildService discord, _) = Build(
            consumeState: new(ChannelId, RedirectUri: null)
        );

        IActionResult result = await controller.HandleDiscordCallback(
            null,
            "nonce",
            "access_denied",
            default
        );

        result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be(Failed("access_denied"));
        discord.UpsertCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Callback_Web_LinkingTheServerFails_RedirectsWithGuildLinkFailed()
    {
        (DiscordOAuthController controller, FakeDiscordGuildService discord, _) = Build(
            consumeState: new(ChannelId, RedirectUri: null)
        );
        discord.FailUpsert = true;

        IActionResult result = await controller.HandleDiscordCallback("c", "nonce", null, default);

        result
            .Should()
            .BeOfType<RedirectResult>()
            .Which.Url.Should()
            .Be(Failed("guild_link_failed"));
    }

    // ─── callback: loopback completion (success / failure) + web fallback ───────

    [Fact]
    public async Task Callback_WithLoopback_Success_RedirectsToLoopbackWithConnectedMarker()
    {
        (DiscordOAuthController controller, FakeDiscordGuildService discord, _) = Build(
            new()
            {
                TokenJson =
                    """{"access_token":"bot-token","refresh_token":"r","expires_in":604800,"guild":{"id":"99","name":"Cool Guild"}}""",
            },
            new(ChannelId, Loopback)
        );

        IActionResult result = await controller.HandleDiscordCallback(
            "the-code",
            "nonce",
            null,
            default
        );

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
            new() { TokenStatus = HttpStatusCode.BadRequest },
            new(ChannelId, Loopback)
        );

        IActionResult result = await controller.HandleDiscordCallback(
            "bad-code",
            "nonce",
            null,
            default
        );

        RedirectResult redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().Be($"{Loopback}?error=token_exchange_failed");

        // A failed exchange never persists a guild link.
        discord.UpsertCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Callback_NoLoopback_Success_RedirectsToOAuthRelay()
    {
        (DiscordOAuthController controller, _, _) = Build(
            new()
            {
                TokenJson =
                    """{"access_token":"bot-token","refresh_token":"r","expires_in":604800,"guild":{"id":"99","name":"Cool Guild"}}""",
            },
            new(ChannelId, RedirectUri: null)
        );

        IActionResult result = await controller.HandleDiscordCallback(
            "the-code",
            "nonce",
            null,
            default
        );

        // The web fallback must land on the /oauth-relay seam (popup postMessage + close, or full-page
        // fallback to the app root) — NOT a hand-rolled frontend deep link. The old target embedded the
        // "(dashboard)" route-GROUP folder literal, which the hash-routed Wasm app cannot match → black screen.
        // The relay is a BACKEND-served page (/oauth-relay), so it lands on the public API/access origin
        // (App:BaseUrl here), NOT the separate App:FrontendUrl — which doesn't serve /oauth-relay at all.
        RedirectResult redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().StartWith("https://api.example.test/oauth-relay");
        redirect.Url.Should().Contain("discord_connected=true");
        redirect.Url.Should().EndWith("&return=%2Fapp%2Fsettings%2Fdiscord");
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
        // The stored secret is sealed; the provider opens it. The controller must send the opened value.
        ISystemCredentialsProvider credentials = Substitute.For<ISystemCredentialsProvider>();
        credentials
            .GetClientIdAsync("discord", Arg.Any<CancellationToken>())
            .Returns("discord-client");
        credentials
            .GetAsync("discord", Arg.Any<CancellationToken>())
            .Returns(new SystemAppCredentials("discord-client", OpenedSecret));

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
            credentials,
            config,
            new SingleClientFactory(handler ?? new StubHandler()),
            NullLogger<DiscordOAuthController>.Instance,
            TimeProvider.System,
            state,
            discord
        )
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() },
        };
        return (controller, discord, state);
    }

    /// <summary>A canned Discord token endpoint: returns <see cref="TokenJson"/> at <see cref="TokenStatus"/>.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public string TokenJson { get; init; } =
            """{"access_token":"bot-token","expires_in":604800,"guild":{"id":"99","name":"Cool Guild"}}""";
        public HttpStatusCode TokenStatus { get; init; } = HttpStatusCode.OK;

        /// <summary>The installer's Discord profile and the accounts linked to it, as Discord answers them.</summary>
        public string UserJson { get; init; } = """{"id":"installer-1"}""";
        public string ConnectionsJson { get; init; } = "[]";
        public HttpStatusCode ConnectionsStatus { get; init; } = HttpStatusCode.OK;

        /// <summary>The form the bot posted to the token endpoint, so a test can assert what Discord was sent.</summary>
        public string? SentForm { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/users/@me/connections"))
                return new HttpResponseMessage(ConnectionsStatus)
                {
                    Content = new StringContent(ConnectionsJson, Encoding.UTF8, "application/json"),
                };

            if (path.EndsWith("/users/@me"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(UserJson, Encoding.UTF8, "application/json"),
                };

            SentForm = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(TokenStatus)
            {
                Content = new StringContent(TokenJson, Encoding.UTF8, "application/json"),
            };
        }
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

        public bool FailUpsert { get; set; }

        public Task<Result<DiscordGuildConnectionDto>> UpsertFromOAuthAsync(
            Guid broadcasterId,
            DiscordGuildOAuthResult oauth,
            CancellationToken ct = default
        )
        {
            UpsertCalls.Add(oauth);
            if (FailUpsert)
                return Task.FromResult(
                    Result.Failure<DiscordGuildConnectionDto>("boom", "INTERNAL_ERROR")
                );

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

        public Task<Result<BlastRadiusDto>> GetDisconnectBlastRadiusAsync(
            Guid broadcasterId,
            Guid connectionId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}

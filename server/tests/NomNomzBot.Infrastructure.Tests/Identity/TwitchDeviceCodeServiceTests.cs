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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Infrastructure.Platform.Auth;
using ConfigEntity = NomNomzBot.Domain.Platform.Entities.Configuration;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the no-secret onboarding path on the wire: Device Code Flow sends NomNomzBot's shipped public client
/// id (or a BYOC override) and the requested scopes to Twitch — and <em>never</em> a client secret — then maps
/// each poll response to the right continuation/terminal status. These are the security-load-bearing facts: a
/// leaked secret is impossible because none is sent, and the poll loop only ends on a real authorize/deny/expire.
/// </summary>
public sealed class TwitchDeviceCodeServiceTests
{
    private const string DeviceResponseJson =
        """{"device_code":"DEV-ABC-123","user_code":"WXYZ-7890","verification_uri":"https://www.twitch.tv/activate","interval":5,"expires_in":1800}""";

    private const string TokenResponseJson =
        """{"access_token":"issued-access","refresh_token":"issued-refresh","expires_in":3600,"scope":["user:read:chat","user:write:chat"],"token_type":"bearer"}""";

    private static readonly string[] Scopes = ["user:read:chat", "user:write:chat"];

    [Fact]
    public async Task RequestDeviceCodeAsync_SendsClientIdAndScopes_WithNoSecret_AndParsesTheCode()
    {
        // The shipped public id lives in config (Twitch:ClientId) — the zero-setup default.
        (TwitchDeviceCodeService service, StubHandler wire) = Build(
            ConfigWith("nomnomz-public-id"),
            HttpStatusCode.OK,
            DeviceResponseJson
        );

        DeviceCodeResult? result = await service.RequestDeviceCodeAsync(Scopes);

        // The parsed code carries exactly what the operator/app need to drive the flow.
        result.Should().NotBeNull();
        result!.DeviceCode.Should().Be("DEV-ABC-123");
        result.UserCode.Should().Be("WXYZ-7890");
        result.VerificationUri.Should().Be("https://www.twitch.tv/activate");
        result.Interval.Should().Be(5);
        result.ExpiresAt.Should().BeAfter(DateTime.UtcNow);

        // The request hits the device endpoint and carries the client id + scopes — and NO secret.
        wire.LastUri.Should().Be(new Uri("https://id.twitch.tv/oauth2/device"));
        string body = WebUtility.UrlDecode(wire.LastBody!);
        body.Should().Contain("client_id=nomnomz-public-id");
        body.Should().Contain("scopes=user:read:chat user:write:chat");
        body.Should().NotContain("client_secret");
    }

    [Fact]
    public async Task RequestDeviceCodeAsync_PrefersByokDbClientId_OverShippedConfigDefault()
    {
        // A self-host operator's own app (BYOC) is a system Configuration row; it must win over the shipped default.
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Configurations.Add(
            new ConfigEntity
            {
                BroadcasterId = null,
                Key = "twitch.client_id",
                Value = "byok-operator-id",
            }
        );
        await db.SaveChangesAsync();

        (TwitchDeviceCodeService service, StubHandler wire) = Build(
            ConfigWith("nomnomz-public-id"),
            HttpStatusCode.OK,
            DeviceResponseJson,
            db
        );

        await service.RequestDeviceCodeAsync(Scopes);

        string body = WebUtility.UrlDecode(wire.LastBody!);
        body.Should().Contain("client_id=byok-operator-id");
        body.Should().NotContain("nomnomz-public-id");
    }

    [Fact]
    public async Task RequestDeviceCodeAsync_WhenNoClientId_ReturnsNull_WithoutCallingTwitch()
    {
        // Neither a shipped default nor a BYOC row — wholly unconfigured. Short-circuit, never issue a request.
        (TwitchDeviceCodeService service, StubHandler wire) = Build(
            new ConfigurationBuilder().Build(),
            HttpStatusCode.OK,
            DeviceResponseJson
        );

        DeviceCodeResult? result = await service.RequestDeviceCodeAsync(Scopes);

        result.Should().BeNull();
        wire.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task PollOnceAsync_WhenAuthorized_ReturnsTokens_OnTheDeviceCodeGrant_WithNoSecret()
    {
        (TwitchDeviceCodeService service, StubHandler wire) = Build(
            ConfigWith("nomnomz-public-id"),
            HttpStatusCode.OK,
            TokenResponseJson
        );

        DevicePollOutcome outcome = await service.PollOnceAsync("DEV-ABC-123", Scopes);

        outcome.Status.Should().Be(DevicePollStatus.Authorized);
        outcome.Tokens.Should().NotBeNull();
        outcome.Tokens!.AccessToken.Should().Be("issued-access");
        outcome.Tokens.RefreshToken.Should().Be("issued-refresh");
        outcome.Tokens.Scopes.Should().BeEquivalentTo("user:read:chat", "user:write:chat");
        outcome.Tokens.ExpiresAt.Should().BeAfter(DateTime.UtcNow);

        // The poll uses the device-code grant + the device code, on the shared client id, and sends no secret.
        wire.LastUri.Should().Be(new Uri("https://id.twitch.tv/oauth2/token"));
        string body = WebUtility.UrlDecode(wire.LastBody!);
        body.Should().Contain("grant_type=urn:ietf:params:oauth:grant-type:device_code");
        body.Should().Contain("device_code=DEV-ABC-123");
        body.Should().Contain("client_id=nomnomz-public-id");
        body.Should().NotContain("client_secret");
    }

    [Fact]
    public async Task PollOnceAsync_WhenAuthorizationPending_ReturnsPending_WithNoTokens()
    {
        (TwitchDeviceCodeService service, _) = Build(
            ConfigWith("nomnomz-public-id"),
            HttpStatusCode.BadRequest,
            """{"status":400,"message":"authorization_pending"}"""
        );

        DevicePollOutcome outcome = await service.PollOnceAsync("DEV-ABC-123", Scopes);

        outcome.Status.Should().Be(DevicePollStatus.Pending);
        outcome.Tokens.Should().BeNull();
    }

    [Fact]
    public async Task PollOnceAsync_WhenSlowDown_ReturnsSlowDown()
    {
        (TwitchDeviceCodeService service, _) = Build(
            ConfigWith("nomnomz-public-id"),
            HttpStatusCode.BadRequest,
            """{"status":400,"message":"slow_down"}"""
        );

        DevicePollOutcome outcome = await service.PollOnceAsync("DEV-ABC-123", Scopes);

        outcome.Status.Should().Be(DevicePollStatus.SlowDown);
    }

    [Fact]
    public async Task PollOnceAsync_WhenOperatorDeclines_ReturnsDenied()
    {
        (TwitchDeviceCodeService service, _) = Build(
            ConfigWith("nomnomz-public-id"),
            HttpStatusCode.BadRequest,
            """{"status":400,"message":"access_denied"}"""
        );

        DevicePollOutcome outcome = await service.PollOnceAsync("DEV-ABC-123", Scopes);

        outcome.Status.Should().Be(DevicePollStatus.Denied);
    }

    [Fact]
    public async Task PollOnceAsync_WhenCodeExpired_ReturnsExpired()
    {
        (TwitchDeviceCodeService service, _) = Build(
            ConfigWith("nomnomz-public-id"),
            HttpStatusCode.BadRequest,
            """{"status":400,"message":"expired_token"}"""
        );

        DevicePollOutcome outcome = await service.PollOnceAsync("DEV-ABC-123", Scopes);

        outcome.Status.Should().Be(DevicePollStatus.Expired);
    }

    [Fact]
    public async Task PollOnceAsync_WhenPolledTooSoon_ReturnsPending_WithoutCallingTwitch()
    {
        // The etiquette guard: a second poll for the same code within Twitch's interval must be reported pending
        // WITHOUT a Twitch round-trip, so a fast/duplicate client can't slam the device endpoint (slow_down).
        (TwitchDeviceCodeService service, StubHandler wire) = Build(
            ConfigWith("nomnomz-public-id"),
            HttpStatusCode.BadRequest,
            """{"status":400,"message":"authorization_pending"}"""
        );

        DevicePollOutcome first = await service.PollOnceAsync("DEV-ABC-123", Scopes);
        DevicePollOutcome second = await service.PollOnceAsync("DEV-ABC-123", Scopes);

        first.Status.Should().Be(DevicePollStatus.Pending); // Twitch said authorization_pending
        second.Status.Should().Be(DevicePollStatus.Pending); // throttled — never reached Twitch
        wire.CallCount.Should().Be(1); // only the first poll hit Twitch
    }

    [Fact]
    public async Task Poll_ReSendsTheWidenedScopes_EvenWhenTheStartAndPollAreSeparateInstances()
    {
        // The scope-regrant bug: RequestDeviceCodeAsync is called with the WIDENED set (granted ∪ missing) on
        // the START request, but the poll re-sent only the base login scopes, so Twitch never issued the extra
        // scopes and the "permissions needed" banner never cleared. The requested set MUST survive to the poll —
        // and in production the start and the polls are SEPARATE HTTP requests, so a scoped (per-request) memory
        // loses it. This proves it survives across two instances sharing the SINGLETON DeviceCodeScopeMemory
        // (the real production shape); with a per-instance dict this test fails, exactly as production did.
        AuthDbContext context = AuthTestBuilder.NewContext();
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(context, out _);
        RoutingHandler wire = new(DeviceResponseJson, TokenResponseJson);
        DeviceCodeScopeMemory sharedMemory = new();

        TwitchDeviceCodeService starter = NewService(context, protector, wire, sharedMemory);
        TwitchDeviceCodeService poller = NewService(context, protector, wire, sharedMemory);
        string[] widened =
        [
            "user:read:chat",
            "user:write:chat",
            "channel:manage:vips",
            "user:read:emotes",
        ];

        DeviceCodeResult? code = await starter.RequestDeviceCodeAsync(widened);
        code!.DeviceCode.Should().Be("DEV-ABC-123");

        // A SEPARATE instance polls with only the BASE set — it must recall + re-send the widened set.
        DevicePollOutcome outcome = await poller.PollOnceAsync(
            "DEV-ABC-123",
            ["user:read:chat", "user:write:chat"]
        );

        outcome.Status.Should().Be(DevicePollStatus.Authorized);
        string pollBody = WebUtility.UrlDecode(wire.LastTokenBody!);
        pollBody.Should().Contain("channel:manage:vips");
        pollBody.Should().Contain("user:read:emotes");
    }

    // ── harness ────────────────────────────────────────────────────────────────

    private static IConfiguration ConfigWith(string clientId) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["Twitch:ClientId"] = clientId }
            )
            .Build();

    private static (TwitchDeviceCodeService Service, StubHandler Wire) Build(
        IConfiguration config,
        HttpStatusCode status,
        string body,
        AuthDbContext? db = null
    )
    {
        AuthDbContext context = db ?? AuthTestBuilder.NewContext();
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(context, out _);
        ISystemCredentialsProvider credentials = AuthTestBuilder.CredentialsProvider(
            context,
            protector,
            config
        );
        StubHandler wire = new(status, body);
        TwitchDeviceCodeService service = new(
            credentials,
            new DeviceCodePollThrottle(TimeProvider.System),
            new DeviceCodeScopeMemory(),
            new SingleClientFactory(wire),
            NullLogger<TwitchDeviceCodeService>.Instance,
            TimeProvider.System
        );
        return (service, wire);
    }

    /// <summary>Builds one service instance over a shared wire + scope memory — models one HTTP request's scope.</summary>
    private static TwitchDeviceCodeService NewService(
        AuthDbContext context,
        ITokenProtector protector,
        HttpMessageHandler wire,
        DeviceCodeScopeMemory scopeMemory
    ) =>
        new(
            AuthTestBuilder.CredentialsProvider(
                context,
                protector,
                ConfigWith("nomnomz-public-id")
            ),
            new DeviceCodePollThrottle(TimeProvider.System),
            scopeMemory,
            new SingleClientFactory(wire),
            NullLogger<TwitchDeviceCodeService>.Instance,
            TimeProvider.System
        );

    /// <summary>Records the request URI + body and returns one canned response (status + JSON).</summary>
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }
        public Uri? LastUri { get; private set; }
        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            LastUri = request.RequestUri;
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>Routes by endpoint: the device-authorization URL gets the device JSON, the token URL the token
    /// JSON — so one instance can serve a full request→poll flow and capture the poll body.</summary>
    private sealed class RoutingHandler(string deviceJson, string tokenJson) : HttpMessageHandler
    {
        public string? LastTokenBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            bool isToken = request.RequestUri!.AbsolutePath.EndsWith("/token");
            if (isToken && request.Content is not null)
                LastTokenBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    isToken ? tokenJson : deviceJson,
                    Encoding.UTF8,
                    "application/json"
                ),
            };
        }
    }
}

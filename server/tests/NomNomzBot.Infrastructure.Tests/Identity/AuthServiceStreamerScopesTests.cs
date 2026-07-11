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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Platform.Deployment;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the Charity/Goals EventSub ingest (ROADMAP "Small decided items") actually requests the two scopes
/// its topics need — <c>channel:read:charity</c> and <c>channel:read:goals</c> — as part of the streamer OAuth
/// grant, exactly like the pre-existing <c>channel:read:hype_train</c> entry it sits beside in
/// <see cref="AuthService"/>'s <c>RequiredScopes</c>. This drives the real URL-building path
/// (<see cref="AuthService.GetTwitchOAuthUrl"/>), not a reflected list, so a scope silently dropped from the
/// requested set — which would make every future charity/goal subscribe 403 with "missing scope" — fails here.
/// </summary>
public sealed class AuthServiceStreamerScopesTests
{
    [Fact]
    public async Task GetTwitchOAuthUrl_RequestsCharityAndGoalsScopes_AlongsideHypeTrain()
    {
        AuthService service = Build(ConfigWith(clientId: "public-id", secret: "shh"));

        Result<string> result = await service.GetTwitchOAuthUrl(
            state: "nonce",
            baseUrl: "https://api.example.test"
        );

        result.IsSuccess.Should().BeTrue();
        // Uri.EscapeDataString percent-encodes ':' (%3A) inside the space-joined `scope` query param.
        result.Value.Should().Contain("channel%3Aread%3Acharity");
        result.Value.Should().Contain("channel%3Aread%3Agoals");
        // Regression guard: the pre-existing hype-train scope these two sit beside must still be requested.
        result.Value.Should().Contain("channel%3Aread%3Ahype_train");
    }

    /// <summary>
    /// Proves E1 (subscribe every remaining translator-backed EventSub topic) requests every scope its newly
    /// added topics need — otherwise each one 403s on first subscribe (TwitchEventSubHostedService.SubscribeAsync)
    /// with no way for an already-onboarded streamer to grant it short of the action-required re-grant flow.
    /// </summary>
    [Fact]
    public async Task GetTwitchOAuthUrl_RequestsEveryE1EventSubScope()
    {
        AuthService service = Build(ConfigWith(clientId: "public-id", secret: "shh"));

        Result<string> result = await service.GetTwitchOAuthUrl(
            state: "nonce",
            baseUrl: "https://api.example.test"
        );

        result.IsSuccess.Should().BeTrue();
        string[] expectedScopes =
        [
            "channel:read:ads",
            "channel:read:vips",
            "moderation:read",
            "moderator:manage:automod",
            "moderator:read:automod_settings",
            "moderator:read:blocked_terms",
            "moderator:read:chat_settings",
            "moderator:read:moderators",
            "moderator:read:shield_mode",
            "moderator:read:shoutouts",
            "moderator:read:suspicious_users",
            "moderator:read:unban_requests",
            "moderator:read:vips",
            "moderator:read:warnings",
            "user:read:whispers",
        ];

        foreach (string scope in expectedScopes)
            result.Value.Should().Contain(Uri.EscapeDataString(scope));
    }

    /// <summary>
    /// Proves Guest Star ingest (restored — the E1 commit's "Twitch deprecated it" claim was false against
    /// live docs) requests both read scopes its beta topics need: <c>channel:read:guest_star</c> for the
    /// broadcaster's own sessions and <c>moderator:read:guest_star</c> for channels the bot moderates.
    /// </summary>
    [Fact]
    public async Task GetTwitchOAuthUrl_RequestsBothGuestStarReadScopes()
    {
        AuthService service = Build(ConfigWith(clientId: "public-id", secret: "shh"));

        Result<string> result = await service.GetTwitchOAuthUrl(
            state: "nonce",
            baseUrl: "https://api.example.test"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(Uri.EscapeDataString("channel:read:guest_star"));
        result.Value.Should().Contain(Uri.EscapeDataString("moderator:read:guest_star"));
    }

    /// <summary>
    /// Proves the dashboard moderation page's WRITE controls request the two Helix manage scopes they need —
    /// <c>moderator:manage:blocked_terms</c> (Add/Remove Blocked Term) and <c>moderator:manage:shield_mode</c>
    /// (Update Shield Mode Status). Without them those controls fell back to a local config Twitch never saw
    /// (cosmetic switches); a scope silently dropped here would resurrect that phantom behaviour.
    /// </summary>
    [Fact]
    public async Task GetTwitchOAuthUrl_RequestsTheBlockedTermsAndShieldModeManageScopes()
    {
        AuthService service = Build(ConfigWith(clientId: "public-id", secret: "shh"));

        Result<string> result = await service.GetTwitchOAuthUrl(
            state: "nonce",
            baseUrl: "https://api.example.test"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(Uri.EscapeDataString("moderator:manage:blocked_terms"));
        result.Value.Should().Contain(Uri.EscapeDataString("moderator:manage:shield_mode"));
    }

    /// <summary>
    /// Proves the dashboard unban-request queue's RESOLVE action requests the Helix manage scope it needs
    /// (<c>moderator:manage:unban_requests</c>) — the read scope is already granted; without the manage scope
    /// approving/denying a request would 403.
    /// </summary>
    [Fact]
    public async Task GetTwitchOAuthUrl_RequestsTheUnbanRequestManageScope()
    {
        AuthService service = Build(ConfigWith(clientId: "public-id", secret: "shh"));

        Result<string> result = await service.GetTwitchOAuthUrl(
            state: "nonce",
            baseUrl: "https://api.example.test"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(Uri.EscapeDataString("moderator:manage:unban_requests"));
    }

    /// <summary>
    /// Proves the dashboard per-user enforcement actions request the Helix manage scopes they need —
    /// <c>moderator:manage:warnings</c> (Warn Chat User) and <c>moderator:manage:suspicious_users</c>
    /// (Update Suspicious User). Only the read variants shipped before; without the manage scopes both actions
    /// would 403.
    /// </summary>
    [Fact]
    public async Task GetTwitchOAuthUrl_RequestsTheWarningsAndSuspiciousUsersManageScopes()
    {
        AuthService service = Build(ConfigWith(clientId: "public-id", secret: "shh"));

        Result<string> result = await service.GetTwitchOAuthUrl(
            state: "nonce",
            baseUrl: "https://api.example.test"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(Uri.EscapeDataString("moderator:manage:warnings"));
        result.Value.Should().Contain(Uri.EscapeDataString("moderator:manage:suspicious_users"));
    }

    // ─── scaffolding (mirrors AuthServiceBotDeviceTests.Build/ConfigWith) ──────────────────────────────

    private static AuthService Build(IConfiguration config)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(db, out _);
        ISystemCredentialsProvider credentials = AuthTestBuilder.CredentialsProvider(
            db,
            protector,
            config
        );

        return new AuthService(
            db,
            Substitute.For<ITwitchAuthService>(),
            Substitute.For<ITwitchDeviceCodeService>(),
            Substitute.For<IIntegrationTokenVault>(),
            Substitute.For<ISessionService>(),
            new RecordingEventBus(),
            credentials,
            Substitute.For<IHttpClientFactory>(),
            config,
            new DeploymentContext(DeploymentMode.SelfHostLite),
            TimeProvider.System,
            NullLogger<AuthService>.Instance
        );
    }

    private static IConfiguration ConfigWith(string clientId, string? secret)
    {
        Dictionary<string, string?> values = new() { ["Twitch:ClientId"] = clientId };
        if (secret is not null)
            values["Twitch:ClientSecret"] = secret;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}

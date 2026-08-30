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
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Progressive scopes (CLAUDE.md "Progressive scopes" / identity-auth §3.4a): a fresh streamer login (or
/// device-code start) must request only the minimal base grant — identity, the two scopes basic chat
/// operation cannot work without, and the moderated-channels list the channel switcher needs — never the whole 79-scope catalogue up front. A 79-scope, 2301-char
/// authorize URL made Twitch's own upstream 502; this is the fix. Every feature scope is instead reachable
/// on demand through the existing action-required mechanism: <c>ScopeNotificationService.GetMissingScopesAsync</c>
/// (the dashboard "N more permissions" banner) and its additive re-grant (<c>BuildRegrantScopeSetAsync</c> →
/// <c>StartTwitchDeviceLoginForScopesAsync</c>), which never forces a logout.
/// </summary>
public sealed class AuthServiceStreamerScopesTests
{
    private static readonly string[] ExpectedMinimalScopes =
    [
        "user:read:email",
        "user:read:chat",
        "user:write:chat",
        "user:read:moderated_channels",
    ];

    [Fact]
    public async Task GetTwitchOAuthUrl_WithoutBroadcasterHint_RequestsOnlyTheMinimalScopeSet()
    {
        AuthService service = Build(ConfigWith(clientId: "public-id", secret: "shh"));

        Result<string> result = await service.GetTwitchOAuthUrl(
            state: "nonce",
            baseUrl: "https://api.example.test"
        );

        result.IsSuccess.Should().BeTrue();

        Uri uri = new(result.Value);
        string? scopeQueryPair = uri
            .Query.TrimStart('?')
            .Split('&')
            .FirstOrDefault(pair => pair.StartsWith("scope=", StringComparison.Ordinal));
        scopeQueryPair.Should().NotBeNull();
        string scopeParam = Uri.UnescapeDataString(scopeQueryPair["scope=".Length..]);
        string[] requestedScopes = scopeParam.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        requestedScopes
            .Should()
            .BeEquivalentTo(
                ExpectedMinimalScopes,
                "login must be minimal, not the whole catalogue"
            );
        result
            .Value.Length.Should()
            .BeLessThan(1000, "the oversized authorize URL 502'd on Twitch's end");
    }

    /// <summary>
    /// The full Helix-reflected catalogue stays intact and reachable — it is what feature-driven, on-demand
    /// scope requests (the missing-scope banner / regrant) check against, not what login requests.
    /// </summary>
    [Fact]
    public void TwitchScopeRegistry_FullCatalogue_StillCarriesEveryDeclaredAndResidualScope()
    {
        TwitchScopeRegistry registry = new();

        registry.AllDeclaredScopes.Should().NotBeEmpty();
        foreach (string scope in registry.AllDeclaredScopes)
            registry.FullCatalogue.Should().Contain(scope);
        foreach (string scope in TwitchScopeRegistry.ResidualEventSubScopes)
            registry.FullCatalogue.Should().Contain(scope);
        registry
            .FullCatalogue.Count.Should()
            .BeGreaterThan(
                60,
                "the full catalogue is the pre-existing 79-scope set, just no longer requested up front"
            );
    }

    /// <summary>
    /// Proves the additive re-auth for a KNOWN channel (the redirect re-grant a returning operator gets):
    /// with a broadcaster hint the requested set is <c>minimal base ∪ currently-granted ∪ recorded-missing</c>
    /// — the extra scope the connection already holds (<c>user:bot</c>, single-account self-host) is
    /// re-consented instead of silently dropped, and a runtime-detected gap is finally requested. Without the
    /// hint (a fresh login) neither rides along, and no un-granted feature scope is requested speculatively.
    /// </summary>
    [Fact]
    public async Task GetTwitchOAuthUrl_WithBroadcasterHint_UnionsMinimalBaseGrantedAndRecordedGaps()
    {
        Guid channel = Guid.Parse("0192a000-0000-7000-8000-0000000000e1");
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.IntegrationConnections.Add(
            new()
            {
                BroadcasterId = channel,
                Provider = "twitch",
                Status = "connected",
                Scopes = ["user:read:email", "user:bot"], // user:bot is NOT in the minimal base set
            }
        );
        db.ChannelMissingScopes.Add(
            new()
            {
                BroadcasterId = channel,
                Scope = "channel:manage:raids", // a feature scope not yet granted — the action-required signal
                DetectedAt = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc),
            }
        );
        await db.SaveChangesAsync();
        AuthService service = Build(ConfigWith(clientId: "public-id", secret: "shh"), db);

        Result<string> withHint = await service.GetTwitchOAuthUrl(
            state: "nonce",
            baseUrl: "https://api.example.test",
            broadcasterHint: channel
        );
        Result<string> without = await service.GetTwitchOAuthUrl(
            state: "nonce",
            baseUrl: "https://api.example.test"
        );

        withHint.IsSuccess.Should().BeTrue();
        withHint.Value.Should().Contain(Uri.EscapeDataString("user:bot"));
        withHint.Value.Should().Contain(Uri.EscapeDataString("channel:manage:raids"));
        withHint.Value.Should().Contain(Uri.EscapeDataString("user:read:chat")); // minimal base still rides
        without.Value.Should().NotContain(Uri.EscapeDataString("user:bot"));
        without.Value.Should().NotContain(Uri.EscapeDataString("channel:manage:raids"));
        // The unrequested, un-granted, un-flagged rest of the catalogue never rides along speculatively.
        withHint.Value.Should().NotContain(Uri.EscapeDataString("channel:manage:polls"));
    }

    /// <summary>
    /// The regression this guards: SelfHostLite's "Log in" device-code button (no broadcaster hint yet — the
    /// operator hasn't authenticated) used to always request the bare <see cref="ExpectedMinimalScopes"/>, so a
    /// returning streamer re-logging in silently narrowed their connection back to the login minimum, dropping
    /// every scope a progressive re-grant had added and re-firing their missing-scope chat notices as if freshly
    /// detected. SelfHostLite has at most one channel, so the existing connection's scopes are already knowable
    /// before the device code is requested — the fix unions them in, so a repeat login is a superset, never a
    /// downgrade.
    /// </summary>
    [Fact]
    public async Task StartTwitchDeviceLoginAsync_ForSelfHostLiteWithAnExistingConnection_UnionsItsScopes()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.IntegrationConnections.Add(
            new()
            {
                BroadcasterId = Guid.Parse("0192a000-0000-7000-8000-0000000000e2"),
                Provider = "twitch",
                Status = "connected",
                // Neither is in the minimal base set — a prior additive re-grant added them.
                Scopes = ["user:read:email", "channel:manage:raids", "moderator:read:followers"],
            }
        );
        await db.SaveChangesAsync();

        ITwitchDeviceCodeService deviceCode = Substitute.For<ITwitchDeviceCodeService>();
        deviceCode
            .RequestDeviceCodeAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(
                new DeviceCodeResult(
                    "device-code",
                    "USER-CODE",
                    "https://twitch.tv/activate",
                    5,
                    DateTime.UtcNow.AddMinutes(5)
                )
            );
        AuthService service = Build(
            ConfigWith(clientId: "public-id", secret: "shh"),
            db,
            deviceCode
        );

        Result<DeviceCodeStartDto> result = await service.StartTwitchDeviceLoginAsync();

        result.IsSuccess.Should().BeTrue();
        await deviceCode
            .Received(1)
            .RequestDeviceCodeAsync(
                Arg.Is<IReadOnlyList<string>>(scopes =>
                    scopes.Contains("channel:manage:raids")
                    && scopes.Contains("moderator:read:followers")
                    && scopes.Contains("user:read:chat") // minimal base still rides
                ),
                Arg.Any<CancellationToken>()
            );
    }

    // ─── scaffolding (mirrors AuthServiceBotDeviceTests.Build/ConfigWith) ──────────────────────────────

    private static AuthService Build(
        IConfiguration config,
        AuthDbContext? existingDb = null,
        ITwitchDeviceCodeService? deviceCode = null
    )
    {
        AuthDbContext db = existingDb ?? AuthTestBuilder.NewContext();
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(db, out _);
        ISystemCredentialsProvider credentials = AuthTestBuilder.CredentialsProvider(
            db,
            protector,
            config
        );

        return new(
            db,
            Substitute.For<ITwitchAuthService>(),
            deviceCode ?? Substitute.For<ITwitchDeviceCodeService>(),
            Substitute.For<IIntegrationTokenVault>(),
            Substitute.For<ISessionService>(),
            Substitute.For<ISessionRevocationService>(),
            new RecordingEventBus(),
            credentials,
            Substitute.For<IHttpClientFactory>(),
            config,
            new(DeploymentMode.SelfHostLite),
            TimeProvider.System,
            new(),
            Substitute.For<IPlatformOwnerPrincipalMinter>(),
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

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

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
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Structural guard for the exact drift that let <c>moderator:manage:shoutouts</c> go missing from the
/// login scope request for weeks: every scope a Helix sub-client actually enforces at runtime
/// (<c>[RequiresTwitchScope]</c>, reflected by the real <see cref="TwitchScopeRegistry"/> — not a mock)
/// must also be requested by <see cref="AuthService.GetTwitchOAuthUrl"/>. A new sub-client method that
/// adds a <c>RequireScopeAsync</c> check without the matching <c>[RequiresTwitchScope]</c> attribute (or
/// a scope the union somehow drops) fails this test — it is now structurally impossible to gate a Helix
/// call on a scope the streamer was never asked to grant.
/// </summary>
public sealed class TwitchScopeRegistryCoverageTests
{
    [Fact]
    public async Task GetTwitchOAuthUrl_RequestsEveryScopeDeclaredViaRequiresTwitchScope()
    {
        TwitchScopeRegistry registry = new();
        AuthService service = Build(ConfigWith(clientId: "public-id", secret: "shh"), registry);

        Result<string> result = await service.GetTwitchOAuthUrl(
            state: "nonce",
            baseUrl: "https://api.example.test"
        );

        result.IsSuccess.Should().BeTrue();
        registry.AllDeclaredScopes.Should().NotBeEmpty();
        foreach (string scope in registry.AllDeclaredScopes)
            result
                .Value.Should()
                .Contain(
                    Uri.EscapeDataString(scope),
                    $"'{scope}' is enforced by a [RequiresTwitchScope]-decorated Helix sub-client method "
                        + "and must be requested at login or that call will 403 with a permission the "
                        + "streamer was never asked to grant"
                );
    }

    private static AuthService Build(IConfiguration config, TwitchScopeRegistry registry)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(db, out _);
        ISystemCredentialsProvider credentials = AuthTestBuilder.CredentialsProvider(
            db,
            protector,
            config
        );

        return new(
            db,
            Substitute.For<ITwitchAuthService>(),
            Substitute.For<ITwitchDeviceCodeService>(),
            Substitute.For<IIntegrationTokenVault>(),
            Substitute.For<ISessionService>(),
            Substitute.For<ISessionRevocationService>(),
            new RecordingEventBus(),
            credentials,
            Substitute.For<IHttpClientFactory>(),
            config,
            new(DeploymentMode.SelfHostLite),
            TimeProvider.System,
            registry,
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

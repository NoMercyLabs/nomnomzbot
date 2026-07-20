// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application;
using NomNomzBot.Infrastructure;

namespace NomNomzBot.Api.Tests.Requirements;

/// <summary>
/// Builds the REAL service container (AddApplication + AddInfrastructure) once, so requirement tests can resolve
/// what the app actually registers — not fakes. Forced into SelfHostLite so it composes over SQLite without a
/// live Postgres/Redis, and hosted services are never started (BuildServiceProvider does not run IHostedService).
/// This is the fixture the whole auth→down requirement suite rides on (the Api tests had none).
/// </summary>
public sealed class DiHostFixture : IDisposable
{
    private readonly ServiceProvider _root;

    public IServiceProvider Services => _root;

    public DiHostFixture()
    {
        // 32-byte AES key, base64 (all-zero bytes) — valid shape for token encryption; no secrets under test.
        const string EncryptionKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    // Force the profile so the resolver does NOT probe Postgres/Redis (which would time out here).
                    ["Deployment:Mode"] = "self_host_lite",
                    ["App:DeploymentMode"] = "self_host_lite",
                    ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:",
                    ["Jwt:Secret"] = "dev-secret-key-at-least-32-characters-long!!",
                    ["Jwt:Issuer"] = "nomnomzbot",
                    ["Jwt:Audience"] = "nomnomzbot",
                    ["Encryption:Key"] = EncryptionKey,
                    // Provider client credentials — dummy values so each login/integration provider constructs.
                    ["Twitch:ClientId"] = "test-client-id",
                    ["Twitch:ClientSecret"] = "test-secret",
                    ["Twitch:BotUsername"] = "testbot",
                    ["Kick:ClientId"] = "test-client-id",
                    ["Kick:ClientSecret"] = "test-secret",
                    ["Twitter:ClientId"] = "test-client-id",
                    ["Twitter:ClientSecret"] = "test-secret",
                    ["YouTube:ClientId"] = "test-client-id",
                    ["YouTube:ClientSecret"] = "test-secret",
                    ["YouTube:ApiKey"] = "test-key",
                    ["Spotify:ClientId"] = "test-client-id",
                    ["Spotify:ClientSecret"] = "test-secret",
                    ["Discord:ClientId"] = "test-client-id",
                    ["Discord:ClientSecret"] = "test-secret",
                    ["Discord:PublicKey"] = "test-key",
                }
            )
            .Build();

        ServiceCollection services = new();
        services.AddSingleton(configuration);
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        _root = services.BuildServiceProvider(validateScopes: false);
    }

    public void Dispose() => _root.Dispose();
}

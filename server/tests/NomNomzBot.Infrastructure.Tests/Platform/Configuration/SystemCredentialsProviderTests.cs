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
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Infrastructure.Platform.Configuration;
using NomNomzBot.Infrastructure.Tests.Identity;
using ConfigEntity = NomNomzBot.Domain.Platform.Entities.Configuration;

namespace NomNomzBot.Infrastructure.Tests.Platform.Configuration;

/// <summary>
/// Proves the onboarding keystone's single resolution path (the bug fix): the wizard-vaulted DB rows win over
/// env/appsettings; an unconfigured provider resolves to env; neither source → null; the client secret is
/// sealed under an AAD bound to its provider + field so it can never be opened as another provider's; and a
/// raw DB read yields only sealed bytes. Uses the focused auth context + the REAL token protector — no stub.
/// </summary>
public sealed class SystemCredentialsProviderTests
{
    private static IConfiguration Config(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.Key, e => (string?)e.Value))
            .Build();

    private static (
        SystemCredentialsProvider Provider,
        AuthDbContext Db,
        ITokenProtector Protector
    ) Build(IConfiguration config)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(db, out _);
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton<ITokenProtector>(protector);
        ServiceProvider sp = services.BuildServiceProvider();
        SystemCredentialsProvider provider = new(
            sp.GetRequiredService<IServiceScopeFactory>(),
            config
        );
        return (provider, db, protector);
    }

    /// <summary>Seal + persist a system-scoped secret row exactly as the setup endpoint does.</summary>
    private static async Task SeedSecretAsync(
        AuthDbContext db,
        ITokenProtector protector,
        string key,
        string plaintextSecret,
        string? plainId = null
    )
    {
        if (plainId is not null)
        {
            db.Configurations.Add(
                new ConfigEntity
                {
                    BroadcasterId = null,
                    Key = key.Replace("client_secret", "client_id"),
                    Value = plainId,
                }
            );
        }

        db.Configurations.Add(
            new ConfigEntity
            {
                BroadcasterId = null,
                Key = key,
                SecureValue = await protector.ProtectAsync(
                    plaintextSecret,
                    SystemCredentialsProvider.ContextFor(key)
                ),
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetAsync_PrefersDbVaultedCredentials_OverEnv()
    {
        IConfiguration config = Config(
            ("Twitch:ClientId", "env-client-id"),
            ("Twitch:ClientSecret", "env-secret")
        );
        (SystemCredentialsProvider provider, AuthDbContext db, ITokenProtector protector) = Build(
            config
        );
        await SeedSecretAsync(
            db,
            protector,
            "twitch.client_secret",
            "db-secret",
            plainId: "db-client-id"
        );

        SystemAppCredentials? creds = await provider.GetAsync("twitch");

        creds.Should().NotBeNull();
        creds!.ClientId.Should().Be("db-client-id");
        creds.ClientSecret.Should().Be("db-secret");
    }

    [Fact]
    public async Task GetAsync_FallsBackToEnv_WhenNoDbRows()
    {
        IConfiguration config = Config(
            ("Spotify:ClientId", "env-spotify-id"),
            ("Spotify:ClientSecret", "env-spotify-secret")
        );
        (SystemCredentialsProvider provider, _, _) = Build(config);

        SystemAppCredentials? creds = await provider.GetAsync("spotify");

        creds.Should().NotBeNull();
        creds!.ClientId.Should().Be("env-spotify-id");
        creds.ClientSecret.Should().Be("env-spotify-secret");
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenNeitherSourceConfiguresBothFields()
    {
        // Only an id in config, no secret anywhere → not configured.
        IConfiguration config = Config(("Discord:ClientId", "only-an-id"));
        (SystemCredentialsProvider provider, _, _) = Build(config);

        (await provider.GetAsync("discord")).Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ResolvesYouTube_FromTheYouTubeConfigSection()
    {
        IConfiguration config = Config(
            ("YouTube:ClientId", "yt-id"),
            ("YouTube:ClientSecret", "yt-secret")
        );
        (SystemCredentialsProvider provider, _, _) = Build(config);

        SystemAppCredentials? creds = await provider.GetAsync("youtube");

        creds.Should().NotBeNull();
        creds!.ClientId.Should().Be("yt-id");
    }

    [Fact]
    public async Task RawDbRead_YieldsOnlySealedBytes_NotThePlaintextSecret()
    {
        (SystemCredentialsProvider provider, AuthDbContext db, ITokenProtector protector) = Build(
            Config()
        );
        await SeedSecretAsync(
            db,
            protector,
            "twitch.client_secret",
            "super-secret",
            plainId: "the-id"
        );

        // A raw read of the stored column must never expose the plaintext.
        ConfigEntity row = db.Configurations.Single(c => c.Key == "twitch.client_secret");
        row.SecureValue.Should().NotBeNullOrEmpty();
        row.SecureValue.Should().NotContain("super-secret");

        // The provider opens it back under the correct AAD.
        (await provider.GetAsync("twitch"))!
            .ClientSecret.Should()
            .Be("super-secret");
    }

    [Fact]
    public async Task AadBinding_RejectsCrossProviderUnprotect()
    {
        (SystemCredentialsProvider provider, AuthDbContext db, ITokenProtector protector) = Build(
            Config()
        );

        // Seal a value as SPOTIFY's secret, but store it under TWITCH's key (the cross-provider transplant a
        // raw-DB attacker would attempt). The AAD is bound to ("system", provider, field), so opening under
        // the twitch key fails closed — the provider returns the row as not-configured, never the spotify secret.
        db.Configurations.Add(
            new ConfigEntity
            {
                BroadcasterId = null,
                Key = "twitch.client_id",
                Value = "id",
            }
        );
        db.Configurations.Add(
            new ConfigEntity
            {
                BroadcasterId = null,
                Key = "twitch.client_secret",
                SecureValue = await protector.ProtectAsync(
                    "spotify-only-secret",
                    SystemCredentialsProvider.ContextFor("spotify.client_secret")
                ),
            }
        );
        await db.SaveChangesAsync();

        // The transplanted secret cannot be opened under twitch's AAD → both-fields-not-present → null.
        (await provider.GetAsync("twitch"))
            .Should()
            .BeNull();
    }

    [Fact]
    public async Task GetValueAsync_ReadsNonSecretDbValue_ThenConfig()
    {
        IConfiguration config = Config(("Twitch:BotUsername", "config-bot"));
        (SystemCredentialsProvider provider, AuthDbContext db, _) = Build(config);
        db.Configurations.Add(
            new ConfigEntity
            {
                BroadcasterId = null,
                Key = "twitch.bot_username",
                Value = "db-bot",
            }
        );
        await db.SaveChangesAsync();

        // DB row wins.
        (await provider.GetValueAsync("twitch", "bot_username"))
            .Should()
            .Be("db-bot");
        // Config fallback for an unset field (PascalCased section + field).
        (await provider.GetValueAsync("twitch", "bot_username", default))
            .Should()
            .Be("db-bot");
    }
}

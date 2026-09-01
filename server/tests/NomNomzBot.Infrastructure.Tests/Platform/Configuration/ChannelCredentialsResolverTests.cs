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
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Infrastructure.Platform.Configuration;
using NomNomzBot.Infrastructure.Tests.Identity;
using ConfigEntity = NomNomzBot.Domain.Platform.Entities.Configuration;

namespace NomNomzBot.Infrastructure.Tests.Platform.Configuration;

/// <summary>
/// Proves the BYOC resolution seam (S-BYOC-spotify-a): a channel's own OAuth app credentials win over the
/// platform's app-level ones; an unconfigured channel falls through to the app-level credentials; neither
/// source configuring both fields fails closed with <c>PROVIDER_NOT_CONFIGURED</c> — never a null default
/// or a malformed request. The channel secret is sealed under an AAD bound to the channel + provider, so a
/// raw DB read yields only sealed bytes and a cross-channel/cross-provider transplant cannot be opened.
/// </summary>
public sealed class ChannelCredentialsResolverTests
{
    private static readonly Guid ChannelA = Guid.Parse("0192b000-0000-7000-8000-0000000000a1");
    private static readonly Guid ChannelB = Guid.Parse("0192b000-0000-7000-8000-0000000000b1");

    private static (
        ChannelCredentialsResolver Resolver,
        AuthDbContext Db,
        ITokenProtector Protector
    ) Build(IConfiguration? config = null)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(db, out _);
        config ??= new ConfigurationBuilder().Build();
        ISystemCredentialsProvider systemCredentials = AuthTestBuilder.CredentialsProvider(
            db,
            protector,
            config
        );
        IChannelCredentialsResolver resolver = AuthTestBuilder.ChannelCredentialsResolver(
            db,
            protector,
            systemCredentials
        );
        return ((ChannelCredentialsResolver)resolver, db, protector);
    }

    private static async Task SeedChannelCredentialsAsync(
        AuthDbContext db,
        ITokenProtector protector,
        Guid channelId,
        string provider,
        string clientId,
        string clientSecret
    )
    {
        db.Configurations.Add(
            new()
            {
                BroadcasterId = channelId,
                Key = $"{provider}.client_id",
                Value = clientId,
            }
        );
        db.Configurations.Add(
            new()
            {
                BroadcasterId = channelId,
                Key = $"{provider}.client_secret",
                SecureValue = await protector.ProtectAsync(
                    clientSecret,
                    ChannelCredentialsResolver.ContextFor(channelId, provider)
                ),
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ResolveAsync_PrefersChannelOwnCredentials_OverAppLevel()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Spotify:ClientId"] = "app-level-id",
                    ["Spotify:ClientSecret"] = "app-level-secret",
                }
            )
            .Build();
        (ChannelCredentialsResolver resolver, AuthDbContext db, ITokenProtector protector) = Build(
            config
        );
        await SeedChannelCredentialsAsync(
            db,
            protector,
            ChannelA,
            "spotify",
            "channel-id",
            "channel-secret"
        );

        Result<SystemAppCredentials> result = await resolver.ResolveAsync(ChannelA, "spotify");

        result.IsSuccess.Should().BeTrue();
        result.Value.ClientId.Should().Be("channel-id");
        result.Value.ClientSecret.Should().Be("channel-secret");
    }

    [Fact]
    public async Task ResolveAsync_FallsBackToAppLevel_WhenChannelHasNoCredentials()
    {
        // A non-Spotify provider (discord) is the regression guard here — Spotify's carve-out (below)
        // must not have broken the general fallback-to-app-level behavior every other BYOC provider relies on.
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Discord:ClientId"] = "app-level-id",
                    ["Discord:ClientSecret"] = "app-level-secret",
                }
            )
            .Build();
        (ChannelCredentialsResolver resolver, _, _) = Build(config);

        Result<SystemAppCredentials> result = await resolver.ResolveAsync(ChannelA, "discord");

        result.IsSuccess.Should().BeTrue();
        result.Value.ClientId.Should().Be("app-level-id");
    }

    [Fact]
    public async Task ResolveAsync_NeverFallsBackToAppLevel_ForSpotify_EvenWhenSystemCredentialIsConfigured()
    {
        // Spotify carve-out (owner directive 2026-09-01, S-OWN10): the bot never hosts a shared/system-level
        // Spotify app. A system-level (app-level) Spotify credential existing must NEVER be used — only the
        // channel's own BYOC row counts; absent that, resolution fails PROVIDER_NOT_CONFIGURED immediately.
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Spotify:ClientId"] = "app-level-id",
                    ["Spotify:ClientSecret"] = "app-level-secret",
                }
            )
            .Build();
        (ChannelCredentialsResolver resolver, _, _) = Build(config);

        Result<SystemAppCredentials> result = await resolver.ResolveAsync(ChannelA, "spotify");

        result.IsFailure.Should().BeTrue();
        result
            .ErrorCode.Should()
            .Be(
                "PROVIDER_NOT_CONFIGURED",
                "Spotify must never resolve against a system/app-level credential, BYOC only"
            );
    }

    [Fact]
    public async Task ResolveAsync_Fails_WhenNeitherSourceConfiguresBothFields()
    {
        (ChannelCredentialsResolver resolver, _, _) = Build();

        Result<SystemAppCredentials> result = await resolver.ResolveAsync(ChannelA, "spotify");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("PROVIDER_NOT_CONFIGURED");
    }

    [Fact]
    public async Task ResolveAsync_DoesNotMixChannelClientId_WithAppLevelSecret()
    {
        // Only the channel client id set (no channel secret) — a half-formed channel row must never
        // silently combine with the app-level secret; the whole channel scope falls through. Uses a
        // non-Spotify provider (discord) since Spotify never falls back to app-level at all (see the
        // carve-out tests above).
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Discord:ClientId"] = "app-level-id",
                    ["Discord:ClientSecret"] = "app-level-secret",
                }
            )
            .Build();
        (ChannelCredentialsResolver resolver, AuthDbContext db, _) = Build(config);
        db.Configurations.Add(
            new()
            {
                BroadcasterId = ChannelA,
                Key = "discord.client_id",
                Value = "channel-id-only",
            }
        );
        await db.SaveChangesAsync();

        Result<SystemAppCredentials> result = await resolver.ResolveAsync(ChannelA, "discord");

        result.IsSuccess.Should().BeTrue();
        result
            .Value.ClientId.Should()
            .Be("app-level-id", "a half-configured channel row falls through, never mixes");
    }

    [Fact]
    public async Task ChannelSecret_IsSealedAtRest_AndCannotBeOpenedAsAnotherChannels()
    {
        (ChannelCredentialsResolver resolver, AuthDbContext db, ITokenProtector protector) =
            Build();
        await SeedChannelCredentialsAsync(
            db,
            protector,
            ChannelA,
            "spotify",
            "channel-a-id",
            "channel-a-secret"
        );

        ConfigEntity row = db.Configurations.Single(c =>
            c.Key == "spotify.client_secret" && c.BroadcasterId == ChannelA
        );
        row.SecureValue.Should().NotBeNullOrEmpty();
        row.SecureValue.Should().NotContain("channel-a-secret");

        // Transplant channel A's sealed row under channel B's key — the AAD is bound to the channel id,
        // so opening it under B's scope must fail closed (falls through to app-level/not-configured),
        // never silently leak A's secret to B.
        db.Configurations.Add(
            new()
            {
                BroadcasterId = ChannelB,
                Key = "spotify.client_id",
                Value = "channel-b-id",
            }
        );
        db.Configurations.Add(
            new()
            {
                BroadcasterId = ChannelB,
                Key = "spotify.client_secret",
                SecureValue = row.SecureValue,
            }
        );
        await db.SaveChangesAsync();

        Result<SystemAppCredentials> crossChannel = await resolver.ResolveAsync(
            ChannelB,
            "spotify"
        );

        crossChannel
            .IsFailure.Should()
            .BeTrue("a channel-A-sealed secret must not open under channel B's AAD");
    }
}

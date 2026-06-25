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
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Infrastructure.Platform.Deployment;

namespace NomNomzBot.Infrastructure.Tests.Deployment;

/// <summary>
/// Proves the registration-time mode resolver (deployment-distribution §2 steps 1–2): the explicit override wins
/// over detection (in both snake_case and enum-name spellings), an absent/unconfigured durable tier resolves to
/// lite, and the mode maps to the right DB + cache provider kinds the DI registration binds.
/// </summary>
public sealed class DeploymentModeResolverTests
{
    [Theory]
    [InlineData("self_host_lite", DeploymentMode.SelfHostLite)]
    [InlineData("SelfHostLite", DeploymentMode.SelfHostLite)]
    [InlineData("self_host_full", DeploymentMode.SelfHostFull)]
    [InlineData("saas", DeploymentMode.Saas)]
    public void Override_via_Deployment_Mode_wins_and_is_not_auto_detected(
        string raw,
        DeploymentMode expected
    )
    {
        IConfiguration configuration = Config(("Deployment:Mode", raw));

        (DeploymentMode mode, bool wasAutoDetected) = DeploymentModeResolver.Resolve(configuration);

        mode.Should().Be(expected);
        wasAutoDetected.Should().BeFalse();
    }

    [Fact]
    public void Override_via_App_DeploymentMode_alias_is_honored()
    {
        IConfiguration configuration = Config(("App:DeploymentMode", "self_host_full"));

        (DeploymentMode mode, bool wasAutoDetected) = DeploymentModeResolver.Resolve(configuration);

        mode.Should().Be(DeploymentMode.SelfHostFull);
        wasAutoDetected.Should().BeFalse();
    }

    [Fact]
    public void No_durable_tier_configured_auto_detects_lite()
    {
        // No Postgres / Redis connection strings at all — the zero-dependency default.
        IConfiguration configuration = Config();

        (DeploymentMode mode, bool wasAutoDetected) = DeploymentModeResolver.Resolve(configuration);

        mode.Should().Be(DeploymentMode.SelfHostLite);
        wasAutoDetected.Should().BeTrue();
    }

    [Fact]
    public void Unparseable_override_falls_back_to_auto_detection()
    {
        IConfiguration configuration = Config(("Deployment:Mode", "nonsense"));

        (DeploymentMode mode, bool wasAutoDetected) = DeploymentModeResolver.Resolve(configuration);

        mode.Should().Be(DeploymentMode.SelfHostLite);
        wasAutoDetected.Should().BeTrue();
    }

    [Theory]
    [InlineData(DeploymentMode.SelfHostLite, DbProviderKind.Sqlite, CacheProviderKind.InMemory)]
    [InlineData(DeploymentMode.SelfHostFull, DbProviderKind.Postgres, CacheProviderKind.Redis)]
    [InlineData(DeploymentMode.Saas, DbProviderKind.Postgres, CacheProviderKind.Redis)]
    public void Mode_maps_to_the_correct_provider_kinds(
        DeploymentMode mode,
        DbProviderKind expectedDb,
        CacheProviderKind expectedCache
    )
    {
        DeploymentModeResolver.DbProviderFor(mode).Should().Be(expectedDb);
        DeploymentModeResolver.CacheProviderFor(mode).Should().Be(expectedCache);
    }

    private static IConfiguration Config(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value))
            )
            .Build();
}

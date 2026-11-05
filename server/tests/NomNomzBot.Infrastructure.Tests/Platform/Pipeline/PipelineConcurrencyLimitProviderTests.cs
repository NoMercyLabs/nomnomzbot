// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Options;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Infrastructure.Platform;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Pipeline;

/// <summary>
/// Proves the per-channel pipeline concurrency cap is actually tiered/resource-aware (S-CONCURRENCY-DRAIN),
/// not the old fixed "5 for everyone" — the exact gap that let one slow command's chat/command pipeline
/// stall every other command on the same channel regardless of the channel's subscription tier or the host's
/// own hardware.
/// </summary>
public sealed class PipelineConcurrencyLimitProviderTests
{
    private static readonly Guid Channel = Guid.NewGuid();

    private static IDeploymentProfileService DeploymentOf(
        DeploymentMode mode,
        DbProviderKind provider
    )
    {
        IDeploymentProfileService service = Substitute.For<IDeploymentProfileService>();
        service.Current.Returns(
            new DeploymentProfileSnapshot(
                InstanceId: Guid.NewGuid(),
                Mode: mode,
                WasAutoDetected: true,
                DbProvider: provider,
                CacheProvider: provider == DbProviderKind.Postgres
                    ? CacheProviderKind.Redis
                    : CacheProviderKind.InMemory,
                EventSubTransport: EventSubTransportMode.WebSocket,
                CodeExecutor: CodeExecutorKind.Jint,
                TokenVault: TokenVaultKind.LocalAes,
                ExposureModel: ExposureModelKind.OptInTunnel,
                RlsEnabled: provider == DbProviderKind.Postgres,
                DefaultGuidanceLevel: GuidanceLevel.Novice
            )
        );
        return service;
    }

    private static IBillingTierService BillingOf(string tierKey)
    {
        IBillingTierService billing = Substitute.For<IBillingTierService>();
        billing
            .GetEntitlementAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new EntitlementDto(tierKey, false, false, new Dictionary<string, long>())
                )
            );
        return billing;
    }

    private static PipelineConcurrencyLimitProvider CreateProvider(
        IDeploymentProfileService deployment,
        IBillingTierService billing,
        ScalingOptions? options = null
    ) => new(deployment, billing, Options.Create(options ?? new ScalingOptions()));

    [Fact]
    public async Task SelfHostLite_Sqlite_UsesTheSmallFixedFloor_RegardlessOfCoreCount()
    {
        PipelineConcurrencyLimitProvider provider = CreateProvider(
            DeploymentOf(DeploymentMode.SelfHostLite, DbProviderKind.Sqlite),
            BillingOf("free")
        );

        int limit = await provider.ResolveMaxConcurrentPerChannelAsync(Channel);

        limit.Should().Be(ScalingDefaults.SelfHostLiteMaxConcurrentPerChannel);
    }

    [Fact]
    public async Task SelfHostFull_Postgres_ScalesWithCpuCores_UpToTheCeiling()
    {
        PipelineConcurrencyLimitProvider provider = CreateProvider(
            DeploymentOf(DeploymentMode.SelfHostFull, DbProviderKind.Postgres),
            BillingOf("free")
        );

        int limit = await provider.ResolveMaxConcurrentPerChannelAsync(Channel);

        limit
            .Should()
            .BeInRange(
                ScalingDefaults.SelfHostLiteMaxConcurrentPerChannel,
                ScalingDefaults.SelfHostFullMaxConcurrentPerChannelCeiling
            );
    }

    [Fact]
    public async Task SelfHost_ExplicitOverride_AlwaysWins()
    {
        PipelineConcurrencyLimitProvider provider = CreateProvider(
            DeploymentOf(DeploymentMode.SelfHostFull, DbProviderKind.Postgres),
            BillingOf("free"),
            new ScalingOptions { SelfHostMaxConcurrentPerChannel = 42 }
        );

        int limit = await provider.ResolveMaxConcurrentPerChannelAsync(Channel);

        limit.Should().Be(42);
    }

    [Theory]
    [InlineData("free", 3)]
    [InlineData("premium", 20)]
    public async Task Saas_UsesTheChannelsTier_NotAFlatNumberForEveryone(
        string tierKey,
        int expectedLimit
    )
    {
        PipelineConcurrencyLimitProvider provider = CreateProvider(
            DeploymentOf(DeploymentMode.Saas, DbProviderKind.Postgres),
            BillingOf(tierKey)
        );

        int limit = await provider.ResolveMaxConcurrentPerChannelAsync(Channel);

        limit.Should().Be(expectedLimit);
    }

    [Fact]
    public async Task Saas_UnknownTierKey_FallsBackToTheSaasDefault_NeverUnlimited()
    {
        PipelineConcurrencyLimitProvider provider = CreateProvider(
            DeploymentOf(DeploymentMode.Saas, DbProviderKind.Postgres),
            BillingOf("some-future-tier-not-yet-seeded")
        );

        int limit = await provider.ResolveMaxConcurrentPerChannelAsync(Channel);

        limit.Should().Be(new ScalingOptions().SaasDefaultMaxConcurrentPerChannel);
    }
}

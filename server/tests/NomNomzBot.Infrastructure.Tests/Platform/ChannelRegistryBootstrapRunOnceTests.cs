// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Platform;

/// <summary>
/// Proves the multi-instance fix for the channel-registry bootstrap pass: two API instances starting
/// against one database (a zero-downtime deploy overlap) must not both run the bootstrap query — a
/// duplicate pass wastes DB load and log noise for no benefit since each instance's registry is populated
/// independently either way. Gated by <see cref="IRunOnceGuard"/>; a non-holder must be a clean no-op that
/// leaves ITS OWN registry untouched.
/// </summary>
public sealed class ChannelRegistryBootstrapRunOnceTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f4001");

    private static (
        ChannelRegistryBootstrapService Service,
        IChannelRegistry Registry
    ) BuildInstance(AuthDbContext db, IRunOnceGuard guard)
    {
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton(guard);
        ServiceProvider provider = services.BuildServiceProvider();

        ChannelRegistry registry = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ChannelRegistry>.Instance,
            TimeProvider.System
        );
        ChannelRegistryBootstrapService service = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            registry,
            NullLogger<ChannelRegistryBootstrapService>.Instance
        );
        return (service, registry);
    }

    private static string DatabaseName => Guid.NewGuid().ToString();

    [Fact]
    public async Task An_instance_that_loses_the_startup_race_leaves_its_own_registry_empty()
    {
        string databaseName = DatabaseName;
        AuthDbContext seedDb = AuthTestBuilder.NewContext(databaseName);
        seedDb.Channels.Add(
            new()
            {
                Id = ChannelId,
                Name = "testchannel",
                NameNormalized = "testchannel",
                TwitchChannelId = "123456",
                CreatedAt = DateTime.UtcNow,
            }
        );
        await seedDb.SaveChangesAsync();

        ConcurrentDictionary<string, byte> sharedLeaseStore = new();

        // Instance A is already bootstrapping at startup — its lease sits on the shared store before
        // instance B's StartAsync ever runs.
        IAsyncDisposable? preHeldLease = await new SharedFakeRunOnceGuard(
            sharedLeaseStore
        ).TryAcquireAsync(
            ChannelRegistryBootstrapService.LeaseResourceName,
            TimeSpan.FromMinutes(5),
            CancellationToken.None
        );
        preHeldLease.Should().NotBeNull();

        (ChannelRegistryBootstrapService serviceB, IChannelRegistry registryB) = BuildInstance(
            AuthTestBuilder.NewContext(databaseName),
            new SharedFakeRunOnceGuard(sharedLeaseStore)
        );

        await serviceB.StartAsync(CancellationToken.None);

        // Instance B lost the race: a clean no-op — its OWN registry never got the bootstrap pass.
        registryB.Count.Should().Be(0);

        await preHeldLease.DisposeAsync();

        (ChannelRegistryBootstrapService serviceA, IChannelRegistry registryA) = BuildInstance(
            AuthTestBuilder.NewContext(databaseName),
            new SharedFakeRunOnceGuard(sharedLeaseStore)
        );

        await serviceA.StartAsync(CancellationToken.None);

        // Instance A won the (now-free) lease: exactly one bootstrap pass took effect, and it landed here.
        registryA.Count.Should().Be(1);
        registryA.Get(ChannelId).Should().NotBeNull();
    }

    /// <summary>
    /// S020: the bootstrap query used to filter on <c>TwitchChannelId != null</c> — a Twitch-only
    /// assumption that silently dropped every Kick/YouTube-only channel from the startup pass. A channel
    /// whose only live platform is Kick carries a null <c>TwitchChannelId</c> but a real
    /// <c>ExternalChannelId</c> (the provider-agnostic key), and must be pre-loaded into the registry
    /// exactly like a Twitch channel — proven by the resulting registry STATE, not a non-null return.
    /// </summary>
    [Fact]
    public async Task A_kick_only_channel_with_no_twitch_channel_id_is_bootstrapped_into_the_registry()
    {
        Guid kickChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f4002");
        string databaseName = DatabaseName;
        AuthDbContext seedDb = AuthTestBuilder.NewContext(databaseName);
        seedDb.Channels.Add(
            new()
            {
                Id = kickChannelId,
                Name = "kickonlychannel",
                NameNormalized = "kickonlychannel",
                TwitchChannelId = null,
                Provider = "kick",
                ExternalChannelId = "kick-ext-123",
                CreatedAt = DateTime.UtcNow,
            }
        );
        await seedDb.SaveChangesAsync();

        ConcurrentDictionary<string, byte> sharedLeaseStore = new();
        (ChannelRegistryBootstrapService service, IChannelRegistry registry) = BuildInstance(
            AuthTestBuilder.NewContext(databaseName),
            new SharedFakeRunOnceGuard(sharedLeaseStore)
        );

        await service.StartAsync(CancellationToken.None);

        registry.Count.Should().Be(1);
        registry.Get(kickChannelId).Should().NotBeNull();
    }
}

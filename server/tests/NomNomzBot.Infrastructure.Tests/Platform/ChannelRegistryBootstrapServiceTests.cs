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
/// The channel-registry bootstrap fills THIS process's in-memory registry, so every instance runs it — a
/// blue/green overlap included. It once sat behind the cluster lease, and the colour that lost the startup
/// race kept an empty registry: no commands, timers or triggers until each channel loaded lazily.
/// </summary>
public sealed class ChannelRegistryBootstrapServiceTests
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
    public async Task Both_colours_of_a_deploy_overlap_fill_their_own_registry()
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

        // One lease store for both colours, with the old bootstrap lease name held — the exact moment the
        // leased version left the second colour empty.
        ConcurrentDictionary<string, byte> sharedLeaseStore = new();
        await using IAsyncDisposable? otherColourBooting = await new SharedFakeRunOnceGuard(
            sharedLeaseStore
        ).TryAcquireAsync("channel-registry-bootstrap", TimeSpan.FromMinutes(5));

        (ChannelRegistryBootstrapService blue, IChannelRegistry blueRegistry) = BuildInstance(
            AuthTestBuilder.NewContext(databaseName),
            new SharedFakeRunOnceGuard(sharedLeaseStore)
        );
        (ChannelRegistryBootstrapService green, IChannelRegistry greenRegistry) = BuildInstance(
            AuthTestBuilder.NewContext(databaseName),
            new SharedFakeRunOnceGuard(sharedLeaseStore)
        );

        await Task.WhenAll(
            blue.StartAsync(CancellationToken.None),
            green.StartAsync(CancellationToken.None)
        );

        blueRegistry.Count.Should().Be(1);
        blueRegistry.Get(ChannelId)!.ChannelName.Should().Be("testchannel");
        greenRegistry.Count.Should().Be(1);
        greenRegistry.Get(ChannelId)!.ChannelName.Should().Be("testchannel");
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

        (ChannelRegistryBootstrapService service, IChannelRegistry registry) = BuildInstance(
            AuthTestBuilder.NewContext(databaseName),
            new SharedFakeRunOnceGuard()
        );

        await service.StartAsync(CancellationToken.None);

        registry.Count.Should().Be(1);
        registry.Get(kickChannelId).Should().NotBeNull();
    }
}

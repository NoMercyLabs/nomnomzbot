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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Application.Obs.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// A bridge that is connected to this node but missing from the shared book (evicted by another node's command
/// timeout during a deploy) is listed again with its own connect time, so the bot can reach OBS again.
/// </summary>
public sealed class ObsBridgeRegistryHealServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000ab05");
    private static readonly DateTime ConnectedAt = new(2026, 11, 1, 19, 18, 55, DateTimeKind.Utc);

    [Fact]
    public async Task Every_live_bridge_of_this_node_is_listed_again_with_its_own_connect_time()
    {
        IObsBridgeRegistry registry = Substitute.For<IObsBridgeRegistry>();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton(registry)
            .BuildServiceProvider();
        ObsBridgeRegistryHealService sut = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<ObsBridgeRegistryHealService>.Instance,
            () => [("conn-a", Channel, ConnectedAt)]
        );

        await sut.HealOnceAsync(CancellationToken.None);

        await registry
            .Received(1)
            .RegisterAsync(Channel, "conn-a", ConnectedAt, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_node_with_no_bridge_touches_nothing()
    {
        IObsBridgeRegistry registry = Substitute.For<IObsBridgeRegistry>();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton(registry)
            .BuildServiceProvider();
        ObsBridgeRegistryHealService sut = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<ObsBridgeRegistryHealService>.Instance,
            () => []
        );

        await sut.HealOnceAsync(CancellationToken.None);

        await registry.DidNotReceiveWithAnyArgs().RegisterAsync(default, default!, default);
    }
}

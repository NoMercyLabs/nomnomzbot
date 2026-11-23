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
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NomNomzBot.Api.Extensions;
using NSubstitute;
using StackExchange.Redis;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// A deploy runs two colors at once. The draining color still publishes hub messages, so with Redis
/// configured every hub fan-out must go through the shared Redis backplane to reach clients on the other color.
/// </summary>
public sealed class SignalRBackplaneTests
{
    [Fact]
    public async Task WithRedis_TheHubLifetimeManagerIsTheRedisBackplane_OnTheSharedChannelPrefix()
    {
        IConnectionMultiplexer multiplexer = Substitute.For<IConnectionMultiplexer>();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(multiplexer);

        services.AddApiSignalR(enableDetailedErrors: false);

        ServiceDescriptor lifetimeManager = services.Last(descriptor =>
            descriptor.ServiceType == typeof(HubLifetimeManager<>)
        );
        lifetimeManager.ImplementationType.Should().Be(typeof(RedisHubLifetimeManager<>));

        using ServiceProvider provider = services.BuildServiceProvider();
        RedisOptions options = provider.GetRequiredService<IOptions<RedisOptions>>().Value;
        options.Configuration.ChannelPrefix.ToString().Should().Be("nomnomzbot");
        options.ConnectionFactory.Should().NotBeNull();
        IConnectionMultiplexer connected = await options.ConnectionFactory!(TextWriter.Null);
        connected.Should().BeSameAs(multiplexer);
    }

    [Fact]
    public void WithoutRedis_TheHubLifetimeManagerStaysTheInMemoryDefault()
    {
        ServiceCollection services = new();
        services.AddLogging();

        services.AddApiSignalR(enableDetailedErrors: false);

        ServiceDescriptor lifetimeManager = services.Last(descriptor =>
            descriptor.ServiceType == typeof(HubLifetimeManager<>)
        );
        lifetimeManager.ImplementationType.Should().Be(typeof(DefaultHubLifetimeManager<>));
    }
}

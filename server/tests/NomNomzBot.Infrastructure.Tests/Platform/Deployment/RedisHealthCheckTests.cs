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
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NomNomzBot.Infrastructure.Platform.Deployment;
using NSubstitute;
using StackExchange.Redis;

namespace NomNomzBot.Infrastructure.Tests.Platform.Deployment;

/// <summary>
/// S038: the previous "redis" health check opened a brand-new <see cref="ConnectionMultiplexer"/> on every
/// probe, so it could report healthy purely because a fresh connection attempt happened to succeed — even
/// while the app's real, shared singleton (the one <c>RedisRateLimiterPartitionStore</c> and the distributed
/// cache actually use) was broken. <see cref="RedisHealthCheck"/> takes the multiplexer via constructor
/// injection instead of constructing its own, so these tests drive it entirely through THAT instance's
/// state — proving the verdict tracks the shared connection, not a throwaway one.
/// </summary>
public sealed class RedisHealthCheckTests
{
    [Fact]
    public async Task Reports_degraded_when_the_injected_multiplexer_is_not_connected()
    {
        // A real multiplexer pointed at a port nothing is listening on, with AbortOnConnectFail disabled
        // (the S038 fix) so .Connect() returns instead of throwing — exactly the shape the app's DI
        // registration now produces when Redis is unreachable at boot. IsConnected is false immediately.
        ConfigurationOptions options = new()
        {
            EndPoints = { { "127.0.0.1", 1 } },
            AbortOnConnectFail = false,
            ConnectTimeout = 200,
            ConnectRetry = 0,
        };
        using ConnectionMultiplexer disconnected = await ConnectionMultiplexer.ConnectAsync(
            options
        );
        RedisHealthCheck sut = new(disconnected);

        HealthCheckResult result = await sut.CheckHealthAsync(new());

        result.Status.Should().Be(HealthStatus.Degraded);
        disconnected.IsConnected.Should().BeFalse("nothing is listening on the probed port");
    }

    [Fact]
    public async Task Reports_unhealthy_when_the_injected_connection_reports_connected_but_PING_fails()
    {
        // This is the exact scenario the old implementation could never catch: the shared multiplexer
        // THINKS it is connected (IsConnected = true) but the actual round-trip against it fails — a half-
        // dead connection, a server that stopped answering without tearing down the socket. A fresh
        // ConnectionMultiplexer.Connect() to a healthy endpoint would report healthy in this situation;
        // pinging the real, injected instance does not.
        IDatabase brokenDatabase = Substitute.For<IDatabase>();
        brokenDatabase
            .PingAsync(Arg.Any<CommandFlags>())
            .Returns<Task<TimeSpan>>(_ =>
                throw new RedisConnectionException(
                    ConnectionFailureType.SocketFailure,
                    "simulated half-dead connection"
                )
            );

        IConnectionMultiplexer multiplexer = Substitute.For<IConnectionMultiplexer>();
        multiplexer.IsConnected.Returns(true);
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(brokenDatabase);

        RedisHealthCheck sut = new(multiplexer);

        HealthCheckResult result = await sut.CheckHealthAsync(new());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().BeOfType<RedisConnectionException>();
    }

    [Fact]
    public async Task Reports_healthy_when_the_injected_connection_pings_successfully()
    {
        IDatabase workingDatabase = Substitute.For<IDatabase>();
        workingDatabase.PingAsync(Arg.Any<CommandFlags>()).Returns(TimeSpan.FromMilliseconds(3));

        IConnectionMultiplexer multiplexer = Substitute.For<IConnectionMultiplexer>();
        multiplexer.IsConnected.Returns(true);
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(workingDatabase);

        RedisHealthCheck sut = new(multiplexer);

        HealthCheckResult result = await sut.CheckHealthAsync(new());

        result.Status.Should().Be(HealthStatus.Healthy);
    }
}

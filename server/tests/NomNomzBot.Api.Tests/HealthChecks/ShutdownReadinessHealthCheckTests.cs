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
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using NomNomzBot.Api.HealthChecks;

namespace NomNomzBot.Api.Tests.HealthChecks;

/// <summary>
/// Proves the Z4 shutdown-readiness gate flips the moment <see cref="IHostApplicationLifetime.ApplicationStopping"/>
/// fires, through the REAL <see cref="IHostApplicationLifetime"/> event registration (not a boolean flag
/// flipped by hand) — a real reverse proxy's readiness poll relies on this to stop routing new traffic to an
/// instance that has started graceful shutdown, before anything else has begun tearing down.
/// </summary>
public sealed class ShutdownReadinessHealthCheckTests
{
    [Fact]
    public async Task Before_ApplicationStopping_ReportsHealthy()
    {
        ShutdownReadinessTracker tracker = new();
        ShutdownReadinessHealthCheck check = new(tracker);

        HealthCheckResult result = await check.CheckHealthAsync(new());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task When_ApplicationLifetime_FiresApplicationStopping_TrackerFlipsImmediately_AndTheCheckReportsUnhealthy()
    {
        using CancellationTokenSource applicationStopping = new();
        IHostApplicationLifetime lifetime = new FakeHostApplicationLifetime(
            applicationStopping.Token
        );

        ShutdownReadinessTracker tracker = new();
        tracker.Bind(lifetime);
        ShutdownReadinessHealthCheck check = new(tracker);

        // Before shutdown starts: still ready.
        (await check.CheckHealthAsync(new()))
            .Status.Should()
            .Be(HealthStatus.Healthy);

        // The real host fires this token on ApplicationStopping, BEFORE any IHostedService.StopAsync runs.
        await applicationStopping.CancelAsync();

        HealthCheckResult afterStopping = await check.CheckHealthAsync(new());
        afterStopping.Status.Should().Be(HealthStatus.Unhealthy);
    }

    /// <summary>Wires only <see cref="ApplicationStopping"/> to a caller-supplied token — the one signal this
    /// slice depends on — so the test drives the exact registration <c>ShutdownReadinessTracker.Bind</c> uses.</summary>
    private sealed class FakeHostApplicationLifetime(CancellationToken applicationStopping)
        : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping { get; } = applicationStopping;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() { }
    }
}

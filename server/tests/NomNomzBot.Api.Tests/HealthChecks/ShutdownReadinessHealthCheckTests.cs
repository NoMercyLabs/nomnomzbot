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

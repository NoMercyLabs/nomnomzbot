// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace NomNomzBot.Api.HealthChecks;

/// <summary>
/// Zero-downtime-deploy readiness gate (Z4): tracks whether the host has begun graceful shutdown, so
/// <c>/health/ready</c> can fail IMMEDIATELY once shutdown starts — before the drain window, before the
/// EventSub transport stops, before anything else tears down. A reverse proxy that polls <c>/health/ready</c>
/// stops ROUTING new traffic to this instance the moment it sees the failure, while <c>/health/live</c> (which
/// never consults this state) keeps reporting the process alive so already-routed and in-flight requests are
/// still served during the configured drain window (<c>Deployment:ShutdownTimeoutSeconds</c>,
/// <see cref="Microsoft.Extensions.Hosting.HostOptions.ShutdownTimeout"/>).
/// </summary>
public sealed class ShutdownReadinessTracker
{
    private volatile bool _shuttingDown;

    /// <summary>True from the instant <see cref="IHostApplicationLifetime.ApplicationStopping"/> fires.</summary>
    public bool IsShuttingDown => _shuttingDown;

    /// <summary>Registers on <see cref="IHostApplicationLifetime.ApplicationStopping"/> so the flip happens as
    /// early as possible in the shutdown sequence — ahead of every <c>IHostedService.StopAsync</c>.</summary>
    public void Bind(IHostApplicationLifetime lifetime) =>
        lifetime.ApplicationStopping.Register(() => _shuttingDown = true);
}

/// <summary>
/// The <see cref="IHealthCheck"/> wired onto <c>/health/ready</c> only (never <c>/health/live</c>): reports
/// <see cref="HealthCheckResult.Unhealthy(string?, Exception?, IReadOnlyDictionary{string, object}?)"/> the
/// instant <see cref="ShutdownReadinessTracker.IsShuttingDown"/> flips, so the readiness endpoint's real HTTP
/// status code (mapped 503 by <see cref="ReadinessStatusCodeMap"/>) reflects shutdown state without waiting
/// for any dependency probe.
/// </summary>
public sealed class ShutdownReadinessHealthCheck(ShutdownReadinessTracker tracker) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            tracker.IsShuttingDown
                ? HealthCheckResult.Unhealthy(
                    "Host is shutting down — draining in-flight requests, not accepting new traffic."
                )
                : HealthCheckResult.Healthy("Host is accepting traffic.")
        );
}

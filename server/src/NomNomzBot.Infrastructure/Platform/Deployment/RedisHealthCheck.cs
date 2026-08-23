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
using StackExchange.Redis;

namespace NomNomzBot.Infrastructure.Platform.Deployment;

/// <summary>
/// S038: the previous "redis" health check built and pinged a brand-new <see cref="ConnectionMultiplexer"/>
/// on every probe, so it reported healthy even while the app's real singleton multiplexer — the one every
/// request actually uses, via <see cref="Application.Abstractions.RateLimiting.IRateLimiterPartitionStore"/>
/// and the distributed cache — was disconnected. This check pings THAT singleton instead, so a broken shared
/// connection is reported broken rather than masked by a fresh connection attempt that happens to succeed
/// (or, with <c>AbortOnConnectFail</c> now disabled, happens to be accepted in a disconnected state and never
/// actually retried).
/// </summary>
public sealed class RedisHealthCheck(IConnectionMultiplexer multiplexer) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        if (!multiplexer.IsConnected)
        {
            return HealthCheckResult.Degraded(
                "Redis multiplexer is not connected — serving on the in-process fallback path."
            );
        }

        try
        {
            TimeSpan latency = await multiplexer
                .GetDatabase()
                .PingAsync()
                .WaitAsync(cancellationToken);
            return HealthCheckResult.Healthy(
                $"Redis PING succeeded in {latency.TotalMilliseconds:F0}ms."
            );
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Redis PING against the shared connection failed.",
                ex
            );
        }
    }
}

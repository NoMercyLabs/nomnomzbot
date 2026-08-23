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
using NomNomzBot.Application.Contracts.Platform;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Api.HealthChecks;

/// <summary>
/// Readiness check (S116) for the Twitch EventSub WebSocket connection. Reports
/// <see cref="HealthStatus.Degraded"/> — not Healthy — only once the socket has been disconnected beyond
/// <see cref="EventSubDisconnectTracker.GracePeriod"/>, so the normal ~5 minute reconnect cycle (README
/// "Known Issues": Twitch sends a <c>reconnect</c> message every ~5 min, which is expected behavior) never
/// flaps readiness. A degraded EventSub connection still lets the API serve REST traffic, so this is
/// Degraded rather than Unhealthy — <c>/health/ready</c> maps Degraded to 503 (S116), while <c>/health</c>
/// keeps reporting it at 200 with the detail visible in the JSON body.
/// </summary>
public sealed class EventSubReadinessHealthCheck(
    ITwitchEventSubService eventSubService,
    EventSubDisconnectTracker disconnectTracker
) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        EventSourceHealth health = eventSubService.Health;
        bool sustainedDisconnect = disconnectTracker.IsSustainedDisconnect(health.IsConnected);

        HealthCheckResult result = sustainedDisconnect
            ? HealthCheckResult.Degraded(
                "EventSub WebSocket has been disconnected beyond the normal reconnect window."
            )
            : HealthCheckResult.Healthy(
                health.IsConnected
                    ? "EventSub WebSocket connected."
                    : "EventSub WebSocket reconnecting (within the normal ~5 minute reconnect cycle)."
            );

        return Task.FromResult(result);
    }
}

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

using Microsoft.Extensions.Diagnostics.HealthChecks;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Api.HealthChecks;

/// <summary>
/// Readiness gate for a blue/green deploy (twitch-eventsub §10): a new instance does not report ready until
/// its conduit shard is bound, so the deploy never stops the old instance while nobody else receives. It is
/// a start-up latch with a bounded wait, never a live EventSub health signal — that stays off the "ready"
/// tag (see <see cref="EventSubReadinessHealthCheck"/>).
/// </summary>
public sealed class EventSubHandoverReadinessHealthCheck(IEventSubHandoverReadiness readiness)
    : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            readiness.IsReadyForHandover
                ? HealthCheckResult.Healthy("EventSub can take over (shard bound or not needed).")
                : HealthCheckResult.Unhealthy("EventSub conduit shard is not bound yet.")
        );
}

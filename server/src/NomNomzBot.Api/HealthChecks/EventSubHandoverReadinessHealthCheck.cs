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

// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Api.HealthChecks;

/// <summary>
/// Readiness check (S116): the app is not ready while EF Core migrations against the resolved provider
/// (SQLite on <c>self_host_lite</c>, PostgreSQL on full/SaaS) are still pending — serving traffic against a
/// stale schema is worse than a 503 while the boot-time migration pass (<c>Program.cs</c>) finishes. Reports
/// <see cref="HealthStatus.Unhealthy"/> (not merely Degraded) because a pending migration means the schema
/// the running code expects is not there yet, not a soft-degraded dependency.
/// </summary>
public sealed class PendingMigrationsHealthCheck(AppDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            IEnumerable<string> pending = await dbContext.Database.GetPendingMigrationsAsync(
                cancellationToken
            );
            List<string> pendingList = pending.ToList();
            return pendingList.Count == 0
                ? HealthCheckResult.Healthy("No pending migrations.")
                : HealthCheckResult.Unhealthy(
                    $"{pendingList.Count} pending migration(s): {string.Join(", ", pendingList)}"
                );
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Failed to check pending migrations.", ex);
        }
    }
}

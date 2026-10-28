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
            List<string> pendingList = [.. pending];
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

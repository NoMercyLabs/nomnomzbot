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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Identity.Jobs;

/// <summary>
/// Expires time-boxed platform-support tenant access grants (<c>BeginTenantAccessAsync</c>,
/// stream-admin.md §2) once their <c>ExpiresAt</c> passes — nothing else ever revoked them (S086f). Mirrors
/// the <c>RedemptionTimerExpiryService</c> shape: a periodic scan on a fresh DI scope, clock-driven and
/// cross-tenant. Only scoped support grants (<c>ScopeChannelId</c> set) are in play — unscoped role
/// assignments are permanent platform staffing, not time-boxed access, and are left alone.
/// </summary>
public sealed class TenantAccessGrantExpiryService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<TenantAccessGrantExpiryService> _logger;

    public TenantAccessGrantExpiryService(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        ILogger<TenantAccessGrantExpiryService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TenantAccessGrantExpiryService started");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
                await Task.Delay(TickInterval, _clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tenant access grant expiry tick failed");
            }
        }
    }

    // Internal so tests can drive a single deterministic tick (InternalsVisibleTo is wired).
    internal async Task TickAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        DateTime now = _clock.GetUtcNow().UtcDateTime;

        List<IamRoleAssignment> due = await db
            .IamRoleAssignments.Where(a =>
                a.ScopeChannelId != null
                && a.RevokedAt == null
                && a.ExpiresAt != null
                && a.ExpiresAt <= now
            )
            .ToListAsync(ct);
        if (due.Count == 0)
            return;

        foreach (IamRoleAssignment assignment in due)
            assignment.RevokedAt = now;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Expired {Count} due tenant access grant(s)", due.Count);
    }
}

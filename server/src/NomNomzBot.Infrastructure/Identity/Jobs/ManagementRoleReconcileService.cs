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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;

namespace NomNomzBot.Infrastructure.Identity.Jobs;

/// <summary>
/// Keeps each channel's Twitch-sourced management roles fresh AFTER onboarding (roles-permissions §4). The
/// onboarding seed reconciles once at onboard time; without this, a channel editor a broadcaster adds later (the
/// grant that lets a mod actually edit stream title — Twitch mods cannot, only editors/broadcasters can) would
/// never be picked up until a re-onboard. On a fixed cadence it re-reads each enabled, onboarded channel's
/// moderators + editors and reconciles its <c>ChannelMemberships</c>, prune-safe: a source whose Twitch read
/// fails is not pruned, so a transient error never wipes roles. Reads run on each channel's own broadcaster
/// token (Get Moderators / Get Channel Editors), so no platform-bot readiness gate is needed. Auto-discovered by
/// <c>AddHostedWorkers</c>.
/// </summary>
public sealed class ManagementRoleReconcileService : BackgroundService
{
    // Management roles change rarely; a 10-minute cadence honours a freshly-added editor promptly without
    // hammering Helix (two reads per channel per tick).
    private static readonly TimeSpan ReconcileInterval = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ManagementRoleReconcileService> _logger;

    public ManagementRoleReconcileService(
        IServiceScopeFactory scopeFactory,
        ILogger<ManagementRoleReconcileService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // First tick after one interval — the onboarding seed already covers the initial reconcile, so there is
        // no need to race it on boot.
        using PeriodicTimer timer = new(ReconcileInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ReconcileAllAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex,
                    "Management-role reconcile: tick failed; retrying next interval."
                );
            }
        }
    }

    private async Task ReconcileAllAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        ITwitchManagementSnapshotBuilder snapshotBuilder =
            scope.ServiceProvider.GetRequiredService<ITwitchManagementSnapshotBuilder>();
        IMembershipService memberships =
            scope.ServiceProvider.GetRequiredService<IMembershipService>();

        List<Guid> channels = await db
            .Channels.Where(c => c.Enabled && c.IsOnboarded)
            .Select(c => c.Id)
            .ToListAsync(ct);

        foreach (Guid broadcasterId in channels)
        {
            try
            {
                ManagementSnapshot snapshot = await snapshotBuilder.BuildAsync(broadcasterId, ct);

                // Nothing readable this run (both moderator + editor reads failed) — skip rather than reconcile
                // against an empty, non-authoritative snapshot.
                if (snapshot.AuthoritativeSources.Count == 0)
                    continue;

                Result result = await memberships.SyncManagementFromTwitchAsync(
                    broadcasterId,
                    snapshot.Members,
                    snapshot.AuthoritativeSources,
                    ct
                );
                if (result.IsFailure)
                    _logger.LogWarning(
                        "Management-role reconcile: sync failed for {BroadcasterId}: {Error} ({Code})",
                        broadcasterId,
                        result.ErrorMessage,
                        result.ErrorCode
                    );
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Management-role reconcile: failed for {BroadcasterId}; retrying next interval.",
                    broadcasterId
                );
            }
        }
    }
}

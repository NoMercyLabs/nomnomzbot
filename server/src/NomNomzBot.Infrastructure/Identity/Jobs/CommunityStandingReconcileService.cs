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
/// Keeps each channel's Plane-A community standings (Subscriber / VIP) fresh AFTER onboarding (roles-permissions
/// §3.5). The onboarding seed sets standings once; without this, a viewer who subscribes later — or whose sub lapses
/// or VIP is removed — would keep a stale standing until a re-onboard (a lapsed sub still reading as Subscriber is
/// the reported bug). The Plane-A sibling of <see cref="ManagementRoleReconcileService"/>: on a fixed cadence it
/// re-reads each enabled, onboarded channel's subscribers + VIPs and reconciles its
/// <c>ChannelCommunityStandings</c>, prune-safe — a signal whose Twitch read fails or is partial never downgrades a
/// standing, and only Helix-seeded sub/VIP rows are managed (a manual Artist or a Moderator standing is untouched).
/// Reads run on each channel's own broadcaster token (Get Broadcaster Subscriptions / Get VIPs), so no platform-bot
/// readiness gate is needed. Auto-discovered by <c>AddHostedWorkers</c>.
/// </summary>
public sealed class CommunityStandingReconcileService : BackgroundService
{
    // Standings change rarely; a 10-minute cadence honours a fresh sub/VIP (or a lapse) promptly without hammering
    // Helix (two reads per channel per tick), matching the Plane-B management reconcile.
    private static readonly TimeSpan ReconcileInterval = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CommunityStandingReconcileService> _logger;

    public CommunityStandingReconcileService(
        IServiceScopeFactory scopeFactory,
        ILogger<CommunityStandingReconcileService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // First tick after one interval — the onboarding seed already covers the initial standings, so there is no
        // need to race it on boot.
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
                    "Community-standing reconcile: tick failed; retrying next interval."
                );
            }
        }
    }

    private async Task ReconcileAllAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        ITwitchStandingSnapshotBuilder snapshotBuilder =
            scope.ServiceProvider.GetRequiredService<ITwitchStandingSnapshotBuilder>();
        ICommunityStandingService standings =
            scope.ServiceProvider.GetRequiredService<ICommunityStandingService>();

        List<Guid> channels = await db
            .Channels.Where(c => c.Enabled && c.IsOnboarded)
            .Select(c => c.Id)
            .ToListAsync(ct);

        foreach (Guid broadcasterId in channels)
        {
            try
            {
                CommunityStandingSnapshot snapshot = await snapshotBuilder.BuildAsync(
                    broadcasterId,
                    ct
                );

                // Nothing readable this run (both the subscriber and VIP reads failed) — skip rather than reconcile
                // against an empty, non-authoritative snapshot.
                if (
                    !snapshot.SubscribersAuthoritative
                    && !snapshot.VipsAuthoritative
                    && snapshot.Members.Count == 0
                )
                    continue;

                Result result = await standings.ReconcileTwitchStandingsAsync(
                    broadcasterId,
                    snapshot,
                    ct
                );
                if (result.IsFailure)
                    _logger.LogWarning(
                        "Community-standing reconcile: sync failed for {BroadcasterId}: {Error} ({Code})",
                        broadcasterId,
                        result.ErrorMessage,
                        result.ErrorCode
                    );
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Community-standing reconcile: failed for {BroadcasterId}; retrying next interval.",
                    broadcasterId
                );
            }
        }
    }
}

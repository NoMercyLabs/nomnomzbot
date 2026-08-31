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
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Domain.Giveaways.Entities;

namespace NomNomzBot.Infrastructure.Giveaways;

/// <summary>
/// The `ClosesAt`-schedule enforcement: every minute, any <c>open</c> giveaway whose
/// <c>ScheduledCloseAt</c> has passed closes automatically — the same transition
/// <c>GiveawayService.CloseAsync</c> makes manually (status flip + <c>ClosesAt</c> stamped to the real
/// close moment), so a broadcaster no longer has to be online at the exact minute a giveaway should end.
/// Idempotent under <see cref="IRunOnceGuard"/> so multi-instance deployments sweep once, exactly like
/// <see cref="GiveawayClaimSweepWorker"/>.
/// </summary>
public sealed class GiveawayAutoCloseWorker : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<GiveawayAutoCloseWorker> _logger;

    public GiveawayAutoCloseWorker(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        ILogger<GiveawayAutoCloseWorker> logger
    )
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TickInterval, _clock);
        try
        {
            do
            {
                try
                {
                    await SweepAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Giveaway auto-close sweep failed");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Host shutdown.
        }
    }

    // Internal (not private) so tests can drive a single deterministic sweep — InternalsVisibleTo
    // (NomNomzBot.Infrastructure.Tests) is already wired for exactly this seam (GiveawayClaimSweepWorker).
    internal async Task SweepAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IRunOnceGuard guard = scope.ServiceProvider.GetRequiredService<IRunOnceGuard>();
        await using IAsyncDisposable? lease = await guard.TryAcquireAsync(
            "giveaway-autoclose-sweep",
            LeaseTtl,
            ct
        );
        if (lease is null)
            return; // another instance is sweeping.

        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        DateTime now = _clock.GetUtcNow().UtcDateTime;

        List<Giveaway> due = await db
            .Giveaways.Where(g =>
                g.Status == GiveawayStatus.Open
                && g.ScheduledCloseAt != null
                && g.ScheduledCloseAt <= now
            )
            .ToListAsync(ct);
        if (due.Count == 0)
            return;

        foreach (Giveaway giveaway in due)
        {
            giveaway.Status = GiveawayStatus.Closed;
            giveaway.ClosesAt = now;
        }
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Giveaway auto-close sweep closed {Count} giveaway(s)", due.Count);
    }
}

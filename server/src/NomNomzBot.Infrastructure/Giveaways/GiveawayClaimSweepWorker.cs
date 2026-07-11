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
/// The claim-window enforcement (giveaways.md D7): every minute, winners still <c>drawn</c> whose
/// giveaway's <c>ClaimWindowMinutes</c> has elapsed since <c>DrawnAt</c> flip to <c>forfeited</c> —
/// eligible for a re-roll. Idempotent under <see cref="IRunOnceGuard"/> so multi-instance deployments
/// sweep once; a forfeit is a status flip on the append-only history, never a delete.
/// </summary>
public sealed class GiveawayClaimSweepWorker : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<GiveawayClaimSweepWorker> _logger;

    public GiveawayClaimSweepWorker(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        ILogger<GiveawayClaimSweepWorker> logger
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
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Giveaway claim sweep failed");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Host shutdown.
        }
    }

    // Internal (not private) so tests can drive a single deterministic sweep —
    // InternalsVisibleTo(NomNomzBot.Infrastructure.Tests) is already wired for exactly this seam.
    internal async Task SweepAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IRunOnceGuard guard = scope.ServiceProvider.GetRequiredService<IRunOnceGuard>();
        await using IAsyncDisposable? lease = await guard.TryAcquireAsync(
            "giveaway-claim-sweep",
            LeaseTtl,
            ct
        );
        if (lease is null)
            return; // another instance is sweeping.

        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        DateTime now = _clock.GetUtcNow().UtcDateTime;

        // Overdue = drawn winner + giveaway with a window + DrawnAt older than the window.
        List<GiveawayWinner> overdue = await db
            .GiveawayWinners.Where(w => w.Status == GiveawayWinnerStatus.Drawn)
            .Join(
                db.Giveaways.Where(g => g.ClaimWindowMinutes != null),
                w => w.GiveawayId,
                g => g.Id,
                (w, g) => new { Winner = w, g.ClaimWindowMinutes }
            )
            .Where(x => x.Winner.DrawnAt.AddMinutes(x.ClaimWindowMinutes!.Value) < now)
            .Select(x => x.Winner)
            .ToListAsync(ct);
        if (overdue.Count == 0)
            return;

        foreach (GiveawayWinner winner in overdue)
            winner.Status = GiveawayWinnerStatus.Forfeited;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Giveaway claim sweep forfeited {Count} unclaimed winner(s)",
            overdue.Count
        );
    }
}

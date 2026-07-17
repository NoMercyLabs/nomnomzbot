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
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Domain.Economy.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Games;

/// <summary>
/// The live-games wall clock (live-games.md §3.1): a 1-second sweep that closes lobbies at
/// <c>JoinClosesAt</c> and drives each session's ticks at its manifest interval — one loop for every game,
/// never a timer per session. On startup, under <see cref="IRunOnceGuard"/>, it sweeps non-terminal
/// <c>GameSession</c> rows left by a crash: each is cancelled and its entry fees refunded exactly once (D9).
/// </summary>
public sealed class LiveGameRunner(
    IServiceScopeFactory scopeFactory,
    LiveGameSessionRegistry registry,
    IRunOnceGuard runOnce,
    TimeProvider clock,
    ILogger<LiveGameRunner> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await SweepOrphanedSessionsAsync(stoppingToken);

            using PeriodicTimer timer = new(TimeSpan.FromSeconds(1), clock);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                foreach (LiveGameSessionRuntime runtime in registry.Snapshot())
                {
                    DateTime now = clock.GetUtcNow().UtcDateTime;
                    bool lobbyDue =
                        runtime.Phase == Application.Games.LiveGamePhase.Lobby
                        && now >= runtime.JoinClosesAt;
                    bool tickDue = runtime.NextTickAt is DateTime due && now >= due;
                    if (runtime.Terminal || (!lobbyDue && !tickDue))
                        continue;
                    try
                    {
                        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                        LiveGameEngine engine =
                            scope.ServiceProvider.GetRequiredService<LiveGameEngine>();
                        await engine.AdvanceClockAsync(runtime.BroadcasterId, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(
                            ex,
                            "Live game clock advance failed for session {SessionId}",
                            runtime.SessionId
                        );
                    }
                }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    /// <summary>
    /// D9 crash recovery: any non-terminal session found at startup lost its in-memory state — it cannot
    /// resume, so it is cancelled and every un-settled stake refunded (idempotent via the ledger links).
    /// </summary>
    // InternalsVisibleTo(NomNomzBot.Infrastructure.Tests) is already wired for exactly this seam.
    internal async Task SweepOrphanedSessionsAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IRunOnceGuard guard = runOnce;
        await using (
            IAsyncDisposable? lease = await guard.TryAcquireAsync(
                "live-games:startup-sweep",
                TimeSpan.FromMinutes(5),
                ct
            )
        )
        {
            if (lease is null)
                return;

            IApplicationDbContext db =
                scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            IGameService games = scope.ServiceProvider.GetRequiredService<IGameService>();
            IEventBus bus = scope.ServiceProvider.GetRequiredService<IEventBus>();

            List<GameSession> orphans = await db
                .GameSessions.Where(s =>
                    s.DeletedAt == null
                    && (
                        s.Status == GameSessionStatus.Lobby
                        || s.Status == GameSessionStatus.Running
                        || s.Status == GameSessionStatus.Resolving
                    )
                )
                .ToListAsync(ct);
            if (orphans.Count == 0)
                return;

            DateTime now = clock.GetUtcNow().UtcDateTime;
            foreach (GameSession orphan in orphans)
            {
                await games.RefundLiveGameAsync(orphan.BroadcasterId, orphan.Id, ct);
                orphan.Status = GameSessionStatus.Cancelled;
                orphan.CancelReason = "startup_sweep";
                orphan.ResolvedAt = now;
            }
            await db.SaveChangesAsync(ct);

            foreach (GameSession orphan in orphans)
                await bus.PublishAsync(
                    new LiveGameCancelledEvent
                    {
                        BroadcasterId = orphan.BroadcasterId,
                        SessionId = orphan.Id,
                        GameType = orphan.GameType,
                        Reason = "startup_sweep",
                    },
                    ct
                );
            logger.LogInformation(
                "Live-games startup sweep cancelled and refunded {Count} orphaned session(s)",
                orphans.Count
            );
        }
    }
}

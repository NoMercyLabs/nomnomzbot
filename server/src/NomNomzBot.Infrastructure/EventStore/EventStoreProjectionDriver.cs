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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;

namespace NomNomzBot.Infrastructure.EventStore;

/// <summary>
/// The projection driver (event-store.md §3.3): a hosted background service that periodically advances every
/// non-paused projection from its checkpoint to the journal head via <see cref="IProjectionRunner.RunOnceAsync"/>.
/// Without it, projections only advance during the legacy import / a manual rebuild, so events appended after that
/// (chat, redemptions, follows, …) never reach the read models — the dashboard then shows stale or empty data.
/// Tenant projections are driven once per registered channel; global projections once over the platform stream.
/// Guarded by <see cref="IRunOnceGuard"/> so only one instance drives on a multi-instance SaaS deployment
/// (self-host lite = always granted, a no-op lease). Mirrors the <c>TimerSchedulerService</c> hosted registration.
/// </summary>
public sealed class EventStoreProjectionDriver : BackgroundService
{
    // The driver polls; it is not the hot path. A short interval keeps the read models near-live without busy-spin,
    // and each RunOnceAsync drains its projection to the head in one call, so a tick is cheap once caught up.
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRunOnceGuard _runOnceGuard;
    private readonly ILogger<EventStoreProjectionDriver> _logger;

    public EventStoreProjectionDriver(
        IServiceScopeFactory scopeFactory,
        IRunOnceGuard runOnceGuard,
        ILogger<EventStoreProjectionDriver> logger
    )
    {
        _scopeFactory = scopeFactory;
        _runOnceGuard = runOnceGuard;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(Interval);
        // Drive once immediately (drain the backlog accrued while the service was stopped), then on each tick.
        // Each segment (drive + wait) has its own try-catch so no exception from either can escape to the host.
        while (true)
        {
            try
            {
                await DriveAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Projection driver tick failed; retrying next tick.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Projection driver timer wait failed; stopping.");
                return;
            }
        }
    }

    /// <summary>
    /// Advances every projection once. Held internal so a test can drive a single pass deterministically without
    /// the timer. Acquires the run-once lease first (lite always grants); a null lease means another instance is
    /// driving, so this pass is skipped.
    /// </summary>
    internal async Task DriveAsync(CancellationToken cancellationToken)
    {
        await using IAsyncDisposable? lease = await _runOnceGuard.TryAcquireAsync(
            "eventstore-projection-driver",
            LeaseTtl,
            cancellationToken
        );
        if (lease is null)
            return;

        // Projections + the runner are scoped (they use the scoped DbContext), so resolve them inside a fresh
        // scope per pass rather than capturing them in this singleton service.
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IProjectionRunner runner = scope.ServiceProvider.GetRequiredService<IProjectionRunner>();
        IEnumerable<IProjection> projections = scope.ServiceProvider.GetServices<IProjection>();

        // Drive tenant projections per channel in the DB — NOT the in-memory IChannelRegistry, which only holds
        // live-connected channels. Replay must catch up every channel that has journaled events regardless of
        // whether its bot is currently connected (a stale token must not freeze its read models).
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        List<Guid> channelIds = await db.Channels.Select(c => c.Id).ToListAsync(cancellationToken);

        foreach (IProjection projection in projections)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            if (projection.IsGlobal)
            {
                await AdvanceAsync(runner, projection.Name, null, cancellationToken);
            }
            else
            {
                foreach (Guid channelId in channelIds)
                    await AdvanceAsync(runner, projection.Name, channelId, cancellationToken);
            }
        }
    }

    private async Task AdvanceAsync(
        IProjectionRunner runner,
        string projectionName,
        Guid? broadcasterId,
        CancellationToken cancellationToken
    )
    {
        Result<long> result = await runner.RunOnceAsync(
            projectionName,
            broadcasterId,
            cancellationToken
        );
        string scope = broadcasterId?.ToString() ?? "platform";
        if (result.IsFailure)
            // A faulted projection stops at the bad event (RunOnceAsync does not advance past it); surface it so a
            // stuck checkpoint is visible rather than silently frozen. It is retried each tick.
            _logger.LogWarning(
                "Projection {Projection} ({Scope}) faulted while advancing: {Error}",
                projectionName,
                scope,
                result.ErrorMessage
            );
        else if (result.Value > 0)
            _logger.LogInformation(
                "Projection {Projection} ({Scope}) applied {Count} event(s).",
                projectionName,
                scope,
                result.Value
            );
    }
}

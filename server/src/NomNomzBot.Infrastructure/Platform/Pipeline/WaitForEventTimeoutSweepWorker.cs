// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Interfaces;

namespace NomNomzBot.Infrastructure.Platform.Pipeline;

/// <summary>
/// S-PIPE-TREE-d3c: the missing wall-clock half of <c>wait_for_event</c> — every 30 seconds, resumes
/// every run across every channel whose <see cref="IPipelineEngine.ResumeTimedOutWaitsAsync"/> deadline
/// has elapsed (S-PIPE-TREE-d3b's honest-timeout policy: the run is never left parked forever). 30
/// seconds keeps the worst-case wake-up delay small relative to the shortest sane wait
/// (<c>WaitForEventAction</c>'s floor is effectively a positive integer of seconds) without hammering
/// the database — the same interval-tick + <see cref="IRunOnceGuard"/> shape
/// <c>GiveawayClaimSweepWorker</c> uses so multi-instance deployments sweep once, not once-per-instance.
/// </summary>
public sealed class WaitForEventTimeoutSweepWorker : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromSeconds(30);

    internal const string LeaseResourceName = "wait-for-event-timeout-sweep";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<WaitForEventTimeoutSweepWorker> _logger;

    public WaitForEventTimeoutSweepWorker(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        ILogger<WaitForEventTimeoutSweepWorker> logger
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
                    _logger.LogError(ex, "wait_for_event timeout sweep failed");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Host shutdown.
        }
    }

    // Internal (not private) so tests can drive a single deterministic sweep —
    // InternalsVisibleTo(NomNomzBot.Infrastructure.Tests) is already wired for exactly this seam
    // (see GiveawayClaimSweepWorker for the same pattern).
    internal async Task SweepAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IRunOnceGuard guard = scope.ServiceProvider.GetRequiredService<IRunOnceGuard>();
        await using IAsyncDisposable? lease = await guard.TryAcquireAsync(
            LeaseResourceName,
            LeaseTtl,
            ct
        );
        if (lease is null)
            return; // another instance is sweeping.

        IPipelineEngine pipeline = scope.ServiceProvider.GetRequiredService<IPipelineEngine>();
        int resumed = await pipeline.ResumeTimedOutWaitsAsync(ct);
        if (resumed > 0)
            _logger.LogInformation("wait_for_event timeout sweep resumed {Count} run(s)", resumed);
    }
}

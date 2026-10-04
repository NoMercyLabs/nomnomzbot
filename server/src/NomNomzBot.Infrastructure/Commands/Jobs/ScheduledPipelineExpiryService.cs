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
using NomNomzBot.Application.Commands.Services;

namespace NomNomzBot.Infrastructure.Commands.Jobs;

/// <summary>
/// Fires DEFERRED, one-shot pipeline runs when they come due (the durable side of the scheduling primitive). Every
/// tick, <see cref="IScheduledPipelineService.FireDueAsync"/> marks due pending tasks terminal and hands them to
/// the background dispatcher, which runs them through the pipeline engine — so a delayed action (a voice-swap auto-revert, a feather auto-hide) runs at its
/// moment even across a restart. The very first tick after boot is the startup sweep: any task that came due while
/// the process was down fires now (or, if overdue beyond the service's stale-grace window, is expired). Mirrors the
/// <c>RedemptionTimerExpiryService</c> shape: a periodic scan on a fresh DI scope, clock-driven and cross-tenant.
/// </summary>
public sealed class ScheduledPipelineExpiryService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(5);

    /// <summary>How long shutdown waits for in-flight runs before cancelling them (the host's own limit is 30 s).</summary>
    internal static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(20);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IScheduledPipelineDispatcher _dispatcher;
    private readonly TimeProvider _clock;
    private readonly ILogger<ScheduledPipelineExpiryService> _logger;

    public ScheduledPipelineExpiryService(
        IServiceScopeFactory scopeFactory,
        IScheduledPipelineDispatcher dispatcher,
        TimeProvider clock,
        ILogger<ScheduledPipelineExpiryService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _dispatcher = dispatcher;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ScheduledPipelineExpiryService started");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                try
                {
                    await TickAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    // The delay MUST still run on a throwing tick — otherwise a persistent failure spins the
                    // loop hot with zero backoff, hammering whatever just failed.
                    _logger.LogError(ex, "Scheduled pipeline expiry tick failed");
                }

                await Task.Delay(TickInterval, _clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop sweeping first, then give the runs already handed to the dispatcher a bounded time to finish.
        await base.StopAsync(cancellationToken);
        using CancellationTokenSource timeout = new(DrainTimeout, _clock);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token
        );
        await _dispatcher.DrainAsync(linked.Token);
    }

    // Internal so tests can drive a single deterministic tick (InternalsVisibleTo is wired).
    internal async Task TickAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IScheduledPipelineService scheduler =
            scope.ServiceProvider.GetRequiredService<IScheduledPipelineService>();
        int fired = await scheduler.FireDueAsync(ct);
        if (fired > 0)
            _logger.LogInformation("Fired {Count} due scheduled pipeline task(s)", fired);
    }
}

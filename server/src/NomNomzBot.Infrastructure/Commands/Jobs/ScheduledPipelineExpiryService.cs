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
/// tick, <see cref="IScheduledPipelineService.FireDueAsync"/> marks due pending tasks terminal and dispatches them
/// through the pipeline engine — so a delayed action (a voice-swap auto-revert, a feather auto-hide) runs at its
/// moment even across a restart. The very first tick after boot is the startup sweep: any task that came due while
/// the process was down fires now (or, if overdue beyond the service's stale-grace window, is expired). Mirrors the
/// <c>RedemptionTimerExpiryService</c> shape: a periodic scan on a fresh DI scope, clock-driven and cross-tenant.
/// </summary>
public sealed class ScheduledPipelineExpiryService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<ScheduledPipelineExpiryService> _logger;

    public ScheduledPipelineExpiryService(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        ILogger<ScheduledPipelineExpiryService> logger
    )
    {
        _scopeFactory = scopeFactory;
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
                await TickAsync(stoppingToken);
                await Task.Delay(TickInterval, _clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled pipeline expiry tick failed");
            }
        }
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

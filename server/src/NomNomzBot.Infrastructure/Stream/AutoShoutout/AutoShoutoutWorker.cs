// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Pipeline;

namespace NomNomzBot.Infrastructure.Stream.AutoShoutout;

/// <summary>Ticks the <see cref="IAutoShoutoutScheduler"/> every 5 seconds (old-bot loop interval).</summary>
public sealed class AutoShoutoutWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IAutoShoutoutScheduler _scheduler;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AutoShoutoutWorker> _logger;

    public AutoShoutoutWorker(
        IAutoShoutoutScheduler scheduler,
        TimeProvider timeProvider,
        ILogger<AutoShoutoutWorker> logger
    )
    {
        _scheduler = scheduler;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(PollInterval, _timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await _scheduler.ProcessDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto shoutout pass failed");
            }
        }
    }
}

// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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

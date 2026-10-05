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

namespace NomNomzBot.Infrastructure.Stream.AdBreak;

/// <summary>Ticks the <see cref="IAdScheduleWarner"/> every 5 seconds; the warner itself reads Helix only once a minute.</summary>
public sealed class AdScheduleWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IAdScheduleWarner _warner;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AdScheduleWorker> _logger;

    public AdScheduleWorker(
        IAdScheduleWarner warner,
        TimeProvider timeProvider,
        ILogger<AdScheduleWorker> logger
    )
    {
        _warner = warner;
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
                await _warner.ProcessAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ad schedule pass failed");
            }
        }
    }
}

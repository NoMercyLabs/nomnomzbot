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

namespace NomNomzBot.Infrastructure.Stream.Clips;

/// <summary>Ticks the <see cref="IClipDetector"/> every 5 seconds; the detector itself reads Helix only once a minute per channel.</summary>
public sealed class ClipDetectorWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IClipDetector _detector;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ClipDetectorWorker> _logger;

    public ClipDetectorWorker(
        IClipDetector detector,
        TimeProvider timeProvider,
        ILogger<ClipDetectorWorker> logger
    )
    {
        _detector = detector;
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
                await _detector.ProcessAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Clip detection pass failed");
            }
        }
    }
}

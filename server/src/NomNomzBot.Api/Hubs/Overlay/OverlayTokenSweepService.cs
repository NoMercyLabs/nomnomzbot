// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Api.Hubs.Overlay;

/// <summary>
/// Every 15 seconds closes the overlay pages whose token ran out of its grace window. The grace end is only a
/// timestamp on the widget row, so nothing else announces it. Failures are logged and the loop continues.
/// </summary>
public sealed class OverlayTokenSweepService(
    OverlayTokenSweeper sweeper,
    TimeProvider timeProvider,
    ILogger<OverlayTokenSweepService> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(Interval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                int closed = await sweeper.SweepAsync(null, stoppingToken);
                if (closed > 0)
                    logger.LogInformation(
                        "Closed {Count} overlay connection(s) on a retired widget token",
                        closed
                    );
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Overlay token sweep failed; continuing");
            }
        }
    }
}

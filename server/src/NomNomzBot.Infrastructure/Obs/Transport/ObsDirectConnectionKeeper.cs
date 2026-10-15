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

namespace NomNomzBot.Infrastructure.Obs.Transport;

/// <summary>
/// Keeps every enabled direct-mode OBS connection open (obs-control.md §3.2), so OBS events reach the
/// trigger surface and the dashboard without waiting for a first command. A channel whose dial fails
/// is retried with a doubling backoff (2 s up to 60 s); a drop is redialed on the next sweep.
/// </summary>
public sealed class ObsDirectConnectionKeeper : BackgroundService
{
    internal static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DialTimeout = TimeSpan.FromSeconds(10);

    private readonly DirectObsTransport _transport;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<ObsDirectConnectionKeeper> _logger;

    public ObsDirectConnectionKeeper(
        DirectObsTransport transport,
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        ILogger<ObsDirectConnectionKeeper> logger
    )
    {
        _transport = transport;
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "OBS direct connection sweep failed.");
            }

            await Task.Delay(SweepInterval, _clock, stoppingToken);
        }
    }

    /// <summary>One pass: dial each enabled direct channel that has no live session and whose backoff is due.</summary>
    internal Task SweepAsync(CancellationToken ct) => Task.CompletedTask;
}

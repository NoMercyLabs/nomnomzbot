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
using NomNomzBot.Application.CustomEvents.Services;

namespace NomNomzBot.Infrastructure.CustomEvents;

/// <summary>
/// The <c>poll</c> ingress loop (custom-events.md §6, <c>CustomDataPollHostedService</c>). A short ~5 s scan tick
/// opens a fresh scope and runs <see cref="ICustomDataPollService.PollDueSourcesAsync"/>; each source's own
/// <c>PollIntervalSeconds</c> gates its actual fetch inside that pass. One iteration's failure never tears the
/// worker down (logged + retried next tick).
/// </summary>
public sealed class CustomDataPollHostedService(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<CustomDataPollHostedService> logger
) : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(ScanInterval, clock);
        try
        {
            do
            {
                try
                {
                    await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                    ICustomDataPollService poll =
                        scope.ServiceProvider.GetRequiredService<ICustomDataPollService>();
                    await poll.PollDueSourcesAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Custom data poll scan tick failed; retrying next tick.");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Host shutdown.
        }
    }
}

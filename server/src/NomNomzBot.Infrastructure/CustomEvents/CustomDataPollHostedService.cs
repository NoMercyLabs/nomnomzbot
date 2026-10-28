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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.CustomEvents.Services;

namespace NomNomzBot.Infrastructure.CustomEvents;

/// <summary>
/// The <c>poll</c> ingress loop (custom-events.md §6, <c>CustomDataPollHostedService</c>). A short ~5 s scan tick
/// opens a fresh scope and runs <see cref="ICustomDataPollService.PollDueSourcesAsync"/>; each source's own
/// <c>PollIntervalSeconds</c> gates its actual fetch inside that pass. One iteration's failure never tears the
/// worker down (logged + retried next tick).
/// <para>
/// Runs on one instance per cluster: an <see cref="IRunOnceGuard"/> lease (<c>customdata-poll</c>, mirroring the
/// socket worker's <c>customdata-socket</c>) is held across ticks so a multi-replica SaaS deployment does not
/// double-fetch/double-publish; while another instance owns it, this one scans nothing and re-tries next tick.
/// Single-instance self-host always wins the lease (<c>NoOpRunOnceGuard</c>), so it is unaffected.
/// </para>
/// </summary>
public sealed class CustomDataPollHostedService(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<CustomDataPollHostedService> logger
) : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromMinutes(5);

    private IAsyncDisposable? _lease;

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

                    // The lease is held across ticks: while another replica owns it, this one skips the scan and
                    // re-tries next tick. On self-host (NoOpRunOnceGuard) it is always granted.
                    if (_lease is null)
                    {
                        IRunOnceGuard guard =
                            scope.ServiceProvider.GetRequiredService<IRunOnceGuard>();
                        _lease = await guard.TryAcquireAsync(
                            "customdata-poll",
                            LeaseTtl,
                            stoppingToken
                        );
                        if (_lease is null)
                            continue;
                    }

                    ICustomDataPollService poll =
                        scope.ServiceProvider.GetRequiredService<ICustomDataPollService>();
                    await poll.PollDueSourcesAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Custom data poll scan tick failed; retrying next tick.");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Host shutdown.
        }
        finally
        {
            if (_lease is not null)
                await _lease.DisposeAsync();
        }
    }
}

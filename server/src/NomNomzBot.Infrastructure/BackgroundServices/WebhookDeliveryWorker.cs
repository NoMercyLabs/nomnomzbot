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
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Infrastructure.Webhooks;

namespace NomNomzBot.Infrastructure.BackgroundServices;

/// <summary>
/// The outbound webhook retry/dead-letter drain (webhooks.md §3.7). Every 30s it opens a scope and runs the
/// <see cref="WebhookRetryProcessor"/> over the due-retry backlog. One iteration's failure never tears the worker
/// down (logged + retried next tick).
///
/// Each iteration is gated by <see cref="IRunOnceGuard"/> so that when two API instances run against one
/// database (zero-downtime deploy overlap) only one of them re-attempts the due backlog per tick — otherwise
/// both instances would race the same <c>Failed</c>/<c>NextRetryAt</c> rows and POST the same webhook twice to
/// the customer's endpoint, which is externally visible and cannot be undone. A per-row atomic claim would be
/// the finer-grained fix, but that requires touching <see cref="WebhookRetryProcessor"/>'s query, which is out
/// of scope for this slice — so this uses the same coarse per-tick lease as
/// <c>GiveawayClaimSweepWorker</c>. A non-holder is a clean no-op: no throw, no error log — overlap is normal.
/// </summary>
public sealed class WebhookDeliveryWorker(
    IServiceProvider serviceProvider,
    ILogger<WebhookDeliveryWorker> logger
) : BackgroundService
{
    private const int BatchSize = 50;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromSeconds(30);

    // Internal (not private) so tests can pre-hold the same lease to simulate an overlapping instance —
    // InternalsVisibleTo(NomNomzBot.Infrastructure.Tests) is already wired for exactly this seam.
    internal const string LeaseResourceName = "webhook-delivery-drain";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunIterationAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Webhook retry drain iteration failed; retrying next tick.");
            }
        }
    }

    // Internal (not private) so tests can drive a single deterministic iteration —
    // InternalsVisibleTo(NomNomzBot.Infrastructure.Tests) is already wired for exactly this seam.
    internal async Task RunIterationAsync(CancellationToken ct)
    {
        using IServiceScope scope = serviceProvider.CreateScope();
        IRunOnceGuard guard = scope.ServiceProvider.GetRequiredService<IRunOnceGuard>();
        await using IAsyncDisposable? lease = await guard.TryAcquireAsync(
            LeaseResourceName,
            LeaseTtl,
            ct
        );
        if (lease is null)
            return; // another instance is draining this tick.

        WebhookRetryProcessor processor =
            scope.ServiceProvider.GetRequiredService<WebhookRetryProcessor>();
        int processed = await processor.ProcessDueAsync(BatchSize, ct);
        if (processed > 0)
            logger.LogDebug("Webhook retry drain re-attempted {Count} due deliveries.", processed);
    }
}

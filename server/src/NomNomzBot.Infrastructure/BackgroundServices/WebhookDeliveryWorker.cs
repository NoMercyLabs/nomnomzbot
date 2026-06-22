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
using NomNomzBot.Infrastructure.Webhooks;

namespace NomNomzBot.Infrastructure.BackgroundServices;

/// <summary>
/// The outbound webhook retry/dead-letter drain (webhooks.md §3.7). Every 30s it opens a scope and runs the
/// <see cref="WebhookRetryProcessor"/> over the due-retry backlog. One iteration's failure never tears the worker
/// down (logged + retried next tick).
/// </summary>
public sealed class WebhookDeliveryWorker(
    IServiceProvider serviceProvider,
    ILogger<WebhookDeliveryWorker> logger
) : BackgroundService
{
    private const int BatchSize = 50;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using IServiceScope scope = serviceProvider.CreateScope();
                WebhookRetryProcessor processor =
                    scope.ServiceProvider.GetRequiredService<WebhookRetryProcessor>();
                int processed = await processor.ProcessDueAsync(BatchSize, stoppingToken);
                if (processed > 0)
                    logger.LogDebug(
                        "Webhook retry drain re-attempted {Count} due deliveries.",
                        processed
                    );
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
}

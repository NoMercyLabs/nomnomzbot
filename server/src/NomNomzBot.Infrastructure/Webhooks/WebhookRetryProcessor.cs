// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Webhooks.Enums;

namespace NomNomzBot.Infrastructure.Webhooks;

/// <summary>
/// The retry-drain core (webhooks.md §3.7) — the testable unit behind <c>WebhookDeliveryWorker</c>. Scans the
/// <c>(Status, NextRetryAt)</c> index for failed deliveries now due, bumps the attempt, and re-attempts each
/// through the dispatcher (which re-signs + delivers and dead-letters at the cap). Returns the count processed.
/// </summary>
public sealed class WebhookRetryProcessor(
    IApplicationDbContext db,
    IOutboundWebhookDispatcher dispatcher,
    TimeProvider clock
)
{
    public async Task<int> ProcessDueAsync(int batchSize, CancellationToken ct = default)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        List<OutboundWebhookDelivery> due = await db
            .OutboundWebhookDeliveries.Where(d =>
                d.Status == WebhookDeliveryStatus.Failed
                && d.NextRetryAt != null
                && d.NextRetryAt <= now
            )
            .OrderBy(d => d.NextRetryAt)
            .Take(batchSize)
            .ToListAsync(ct);

        foreach (OutboundWebhookDelivery delivery in due)
        {
            delivery.Attempt++;
            await dispatcher.AttemptDeliveryAsync(delivery, ct);
        }
        return due.Count;
    }
}

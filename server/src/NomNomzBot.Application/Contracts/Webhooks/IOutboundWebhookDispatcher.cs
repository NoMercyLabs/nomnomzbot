// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Webhooks.Enums;

namespace NomNomzBot.Application.Contracts.Webhooks;

/// <summary>
/// Outbound webhook delivery (webhooks.md §3.6): renders, Standard-Webhooks-signs, and POSTs through the
/// SSRF-hardened <c>egress-allowlisted</c> client. The first attempt runs inline (fast path); the retry/dead-letter
/// loop is driven by the delivery worker (§3.7).
/// </summary>
public interface IOutboundWebhookDispatcher
{
    /// <summary>Fan-out: enqueue + attempt #1 for every enabled endpoint subscribed to the event.</summary>
    Task<Result<IReadOnlyList<OutboundEnqueueResult>>> EnqueueForEventAsync(
        Guid broadcasterId,
        string eventType,
        IReadOnlyDictionary<string, string> variables,
        Guid? journalEventId,
        CancellationToken ct = default
    );

    /// <summary>Enqueue + attempt a single explicit delivery to one endpoint (the send_webhook action path).</summary>
    Task<Result<OutboundEnqueueResult>> EnqueueForEndpointAsync(
        Guid broadcasterId,
        Guid endpointId,
        string eventType,
        IReadOnlyDictionary<string, string> variables,
        Guid? journalEventId,
        CancellationToken ct = default
    );

    /// <summary>Performs ONE attempt for an existing pending/failed delivery (re-signs with both secrets).</summary>
    Task<Result<WebhookDeliveryStatus>> AttemptDeliveryAsync(
        OutboundWebhookDelivery delivery,
        CancellationToken ct = default
    );
}

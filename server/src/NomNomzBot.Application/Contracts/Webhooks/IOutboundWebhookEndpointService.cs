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

namespace NomNomzBot.Application.Contracts.Webhooks;

/// <summary>
/// Outbound webhook endpoint CRUD (webhooks.md §3.5). Mints + AEAD-seals the <c>whsec_</c> signing secret, and
/// pins each endpoint to an enabled H.7 egress-allowlist row (EGRESS_NOT_ALLOWED otherwise — the URL/secret live
/// here, the SSRF boundary lives on H.7). The plaintext secret is revealed once, at create/rotate.
/// </summary>
public interface IOutboundWebhookEndpointService
{
    Task<Result<PagedList<OutboundWebhookEndpointDto>>> ListAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    );

    Task<Result<OutboundWebhookEndpointDto>> GetAsync(
        Guid broadcasterId,
        Guid endpointId,
        CancellationToken ct = default
    );

    /// <summary>
    /// The catalogue of subscribable business event types (webhooks.md §9) — the closed set an endpoint's
    /// <c>SubscribedEventTypes</c> is validated against, so the dashboard renders a checklist instead of a free-text
    /// box. Webhook-lifecycle events are deliberately absent (self-amplification deny-list). Never fails.
    /// </summary>
    Result<IReadOnlyList<OutboundWebhookEventCatalogueEntry>> GetEventCatalogue();

    /// <summary>Validates the Fqdn against an enabled H.7 row, mints + seals the secret. Reveals the secret once.</summary>
    Task<Result<OutboundWebhookEndpointCreatedDto>> CreateAsync(
        Guid broadcasterId,
        Guid actorUserId,
        CreateOutboundWebhookRequest request,
        CancellationToken ct = default
    );

    Task<Result<OutboundWebhookEndpointDto>> UpdateAsync(
        Guid broadcasterId,
        Guid endpointId,
        UpdateOutboundWebhookRequest request,
        CancellationToken ct = default
    );

    /// <summary>Rotates with overlap: promotes the current secret to secondary, mints a new primary. Reveals it once.</summary>
    Task<Result<OutboundWebhookEndpointCreatedDto>> RotateSecretAsync(
        Guid broadcasterId,
        Guid endpointId,
        CancellationToken ct = default
    );

    /// <summary>Re-enables an auto-disabled endpoint (clears the failure counters).</summary>
    Task<Result<OutboundWebhookEndpointDto>> ReenableAsync(
        Guid broadcasterId,
        Guid endpointId,
        CancellationToken ct = default
    );

    Task<Result> DeleteAsync(Guid broadcasterId, Guid endpointId, CancellationToken ct = default);

    /// <summary>The endpoint's delivery log (webhooks.md §5.1), newest attempt first, paged. NOT_FOUND when the
    /// endpoint doesn't exist under this tenant (never leaks another channel's deliveries).</summary>
    Task<Result<PagedList<OutboundWebhookDeliveryDto>>> ListDeliveriesAsync(
        Guid broadcasterId,
        Guid endpointId,
        PaginationParams pagination,
        CancellationToken ct = default
    );

    /// <summary>Sends a synthetic ping NOW (single attempt) to verify wiring.</summary>
    Task<Result<WebhookTestResultDto>> SendTestAsync(
        Guid broadcasterId,
        Guid endpointId,
        CancellationToken ct = default
    );
}

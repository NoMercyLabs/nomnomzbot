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
/// Inbound webhook endpoint CRUD (webhooks.md §3.1). Mints the opaque URL token and seals the per-provider
/// verification secret through the canonical crypto-shred path (<c>ITokenProtector</c>). Never returns the secret.
/// </summary>
public interface IInboundWebhookEndpointService
{
    Task<Result<PagedList<InboundWebhookEndpointDto>>> ListAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    );

    Task<Result<InboundWebhookEndpointDto>> GetAsync(
        Guid broadcasterId,
        Guid endpointId,
        CancellationToken ct = default
    );

    /// <summary>Mints the token, seals the verification secret, persists. Generic adapter requires a GenericConfig.</summary>
    Task<Result<InboundWebhookEndpointDto>> CreateAsync(
        Guid broadcasterId,
        Guid actorUserId,
        CreateInboundWebhookRequest request,
        CancellationToken ct = default
    );

    /// <summary>Patches the endpoint; re-encrypts the secret when a new one is supplied.</summary>
    Task<Result<InboundWebhookEndpointDto>> UpdateAsync(
        Guid broadcasterId,
        Guid endpointId,
        UpdateInboundWebhookRequest request,
        CancellationToken ct = default
    );

    /// <summary>Rotates the opaque token — the old ingest URL stops resolving immediately.</summary>
    Task<Result<InboundWebhookEndpointDto>> RotateTokenAsync(
        Guid broadcasterId,
        Guid endpointId,
        CancellationToken ct = default
    );

    /// <summary>Soft-deletes the endpoint. NOT_FOUND if absent.</summary>
    Task<Result> DeleteAsync(Guid broadcasterId, Guid endpointId, CancellationToken ct = default);
}

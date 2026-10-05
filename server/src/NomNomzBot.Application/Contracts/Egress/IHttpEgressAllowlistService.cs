// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------
using NomNomzBot.Application.Common.Consequences;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Egress;

namespace NomNomzBot.Application.Contracts.Egress;

/// <summary>
/// Management of a channel's outbound egress allowlist — the hosts the bot may call for outbound webhooks and
/// custom data sources. Every call is scoped to one broadcaster. A host is validated before it is stored.
/// </summary>
public interface IHttpEgressAllowlistService
{
    Task<Result<PagedList<HttpEgressAllowlistDto>>> ListAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    );

    /// <summary>Approve a host (enabled). Fails with VALIDATION_FAILED for a bad host and ALREADY_EXISTS for a duplicate.</summary>
    Task<Result<HttpEgressAllowlistDto>> CreateAsync(
        Guid broadcasterId,
        Guid actorUserId,
        CreateHttpEgressAllowlistRequest request,
        CancellationToken ct = default
    );

    /// <summary>Turn a host on or off. A disabled host is refused by every consumer (EGRESS_NOT_ALLOWED).</summary>
    Task<Result<HttpEgressAllowlistDto>> SetEnabledAsync(
        Guid broadcasterId,
        Guid allowlistId,
        bool isEnabled,
        CancellationToken ct = default
    );

    /// <summary>
    /// Counts what loses this host when it is deleted: outbound webhook endpoints that use it (by link or by host
    /// name) and custom data sources whose URL points at it. Shown BEFORE the delete is confirmed. Fails with
    /// NOT_FOUND for an unknown host, never a zero for a check that did not run.
    /// </summary>
    Task<Result<BlastRadiusDto>> GetDeleteBlastRadiusAsync(
        Guid broadcasterId,
        Guid allowlistId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Soft-delete a host. Every consumer refuses it afterwards (EGRESS_NOT_ALLOWED) and the outbound webhook
    /// endpoints that used it are switched off in the same save, so nothing keeps sending to a removed host.
    /// </summary>
    Task<Result> DeleteAsync(Guid broadcasterId, Guid allowlistId, CancellationToken ct = default);
}

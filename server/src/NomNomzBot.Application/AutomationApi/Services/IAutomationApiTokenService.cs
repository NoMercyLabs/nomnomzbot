// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.AutomationApi.Services;

/// <summary>
/// Management plane for the channel's automation API tokens (automation-api.md §3): issue, rotate,
/// revoke, and list — the secret is minted here, returned exactly once, and persisted only as a hash.
/// </summary>
public interface IAutomationApiTokenService
{
    Task<Result<PagedList<AutomationTokenDto>>> ListAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    );

    /// <summary>Mint a token; the returned secret is shown once and never retrievable again.</summary>
    Task<Result<IssuedAutomationTokenDto>> CreateAsync(
        Guid broadcasterId,
        Guid createdByUserId,
        CreateAutomationTokenRequest request,
        CancellationToken ct = default
    );

    /// <summary>Replace the token's secret; the old secret stops authenticating immediately.</summary>
    Task<Result<IssuedAutomationTokenDto>> RotateAsync(
        Guid broadcasterId,
        Guid tokenId,
        Guid actorUserId,
        CancellationToken ct = default
    );

    /// <summary>Tombstone the token (<c>RevokedAt</c>); idempotent on an already-revoked token.</summary>
    Task<Result<bool>> RevokeAsync(
        Guid broadcasterId,
        Guid tokenId,
        Guid actorUserId,
        CancellationToken ct = default
    );

    /// <summary>The subscribable public event types, for integrators building against the stream.</summary>
    Task<Result<IReadOnlyList<AutomationEventCatalogItem>>> GetEventCatalogAsync(
        CancellationToken ct = default
    );
}

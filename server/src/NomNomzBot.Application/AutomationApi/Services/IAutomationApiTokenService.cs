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

    /// <summary>
    /// Data-plane self-refresh (stream-deck.md D8) — the token being presented rotates ITS OWN secret
    /// in place: same <c>Id</c>/<c>Name</c>/<c>Scopes</c>/<c>AllowedPipelineIds</c>, a fresh
    /// <c>TokenHash</c>/<c>TokenPrefix</c>, and <c>ExpiresAt</c> pushed out another 30 days. The old
    /// secret stops authenticating the instant this returns. No Gate-2 — a valid token may always
    /// refresh itself; a revoked or hard-expired token fails typed (<c>TOKEN_REVOKED</c> /
    /// <c>TOKEN_EXPIRED</c>) rather than silently minting a fresh credential for a dead one.
    /// </summary>
    Task<Result<IssuedAutomationTokenDto>> RefreshSelfAsync(
        Guid broadcasterId,
        Guid tokenId,
        CancellationToken ct = default
    );
}

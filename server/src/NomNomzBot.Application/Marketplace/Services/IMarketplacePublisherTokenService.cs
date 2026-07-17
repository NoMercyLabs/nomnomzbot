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

namespace NomNomzBot.Application.Marketplace.Services;

/// <summary>
/// Custody of the channel's marketplace publisher account token (marketplace.md §4: "the bot stores it
/// vaulted"). Write-only semantics: the token can be set and cleared but is NEVER echoed back — the only
/// readable surface is <see cref="GetStatusAsync"/>'s <c>HasToken</c> flag. Stored through the
/// crypto-shred-ready <c>IIntegrationTokenVault</c> (provider <c>marketplace</c>), so it is AES-256-GCM
/// sealed at rest with no new table.
/// </summary>
public interface IMarketplacePublisherTokenService
{
    /// <summary>Vault (or replace) the channel's publisher token. The plaintext is never persisted.</summary>
    Task<Result> SetTokenAsync(
        Guid broadcasterId,
        string token,
        Guid? actorUserId = null,
        CancellationToken ct = default
    );

    /// <summary>Revoke the stored publisher token. Idempotent — clearing an absent token succeeds.</summary>
    Task<Result> ClearTokenAsync(Guid broadcasterId, CancellationToken ct = default);

    /// <summary>Whether a publisher token is stored — the ONLY read surface exposed to the dashboard.</summary>
    Task<Result<MarketplacePublisherStatusDto>> GetStatusAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Decrypt the token for an outbound marketplace call (publish / submission status). Internal seam for
    /// <see cref="IMarketplaceClient"/> only — never surfaced over REST. Null when absent or unreadable.
    /// </summary>
    Task<string?> GetPublisherTokenAsync(Guid broadcasterId, CancellationToken ct = default);
}

/// <summary>The publisher-token read model: presence only, never the value.</summary>
public sealed record MarketplacePublisherStatusDto(bool HasToken);

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
using NomNomzBot.Application.Identity.Dtos;

namespace NomNomzBot.Application.Identity.Services;

/// <summary>
/// The crypto-shred-ready OAuth token vault (identity-auth §3.4). Owns <c>IntegrationConnection</c> +
/// <c>IntegrationToken</c> and is the single seam for storing/reading provider tokens — replacing all direct
/// <c>Service.AccessToken/RefreshToken</c> access. It sits over the canonical token-protection facade
/// (<c>ITokenProtector</c> → <c>ISubjectKeyService</c> + <c>IFieldCipher</c>); it never hand-rolls crypto.
/// Tokens are AES-256-GCM-sealed at rest and decryptable only while the subject DEK is active (crypto-shred
/// renders them permanently unreadable).
/// </summary>
public interface IIntegrationTokenVault
{
    /// <summary>
    /// Upserts a connection by <c>(BroadcasterId, Provider, ProviderAccountId)</c>. Stores NO secrets. Emits
    /// <c>IntegrationConnectedEvent</c> on first connect.
    /// </summary>
    Task<Result<IntegrationConnectionDto>> UpsertConnectionAsync(
        UpsertConnectionDto request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// AES-256-GCM-encrypts the access/refresh/app tokens under the connection's subject DEK and upserts the
    /// <c>IntegrationToken</c> rows; sets <c>Status=connected</c>, resets the failure count, reconciles the
    /// granted scope set, and emits <c>IntegrationTokenRefreshedEvent</c>.
    /// </summary>
    Task<Result> StoreTokensAsync(
        Guid connectionId,
        StoreTokensDto tokens,
        IReadOnlyList<string>? grantedScopes = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Decrypts the access token for an outbound call. Fails closed when the DEK was crypto-shredded. Does
    /// NOT auto-refresh — refresh is the provider service's job.
    /// </summary>
    Task<Result<DecryptedTokenDto>> GetAccessTokenAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Decrypts the refresh token for a provider-side refresh call. Same shred-failure semantics.</summary>
    Task<Result<DecryptedTokenDto>> GetRefreshTokenAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Increments the consecutive-failure count and stamps <c>LastErrorAt</c>; at the threshold sets
    /// <c>Status=needs_reauth</c> and emits <c>IntegrationNeedsReauthEvent</c>.
    /// </summary>
    Task<Result> MarkRefreshFailureAsync(
        Guid connectionId,
        string error,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Soft-deletes the connection's tokens, sets <c>Status=revoked</c>, and emits
    /// <c>IntegrationDisconnectedEvent</c>. Does NOT destroy the DEK (other rows may share it).
    /// </summary>
    Task<Result> RevokeConnectionAsync(
        Guid connectionId,
        string reason,
        CancellationToken cancellationToken = default
    );

    /// <summary>Lists a tenant's connections (or platform/global when null). Read-only; never returns ciphertext.</summary>
    Task<Result<IReadOnlyList<IntegrationConnectionDto>>> ListConnectionsAsync(
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    );
}

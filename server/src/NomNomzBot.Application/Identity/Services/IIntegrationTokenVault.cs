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
    /// Records a dead-token signal (an OAuth <c>invalid_grant</c>, a 401 on a freshly refreshed token):
    /// increments the consecutive-failure count and stamps <c>LastErrorAt</c>; at the threshold sets
    /// <c>Status=needs_reauth</c> and emits <c>IntegrationNeedsReauthEvent</c>.
    /// </summary>
    Task<Result> MarkRefreshFailureAsync(
        Guid connectionId,
        string error,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Records a refresh the provider refused for good (a dead grant, a rejected client): sets
    /// <c>Status=needs_reauth</c> at once, bumps the failure count, and emits <c>IntegrationNeedsReauthEvent</c>
    /// so the owner gets one reconnect prompt. A connection that is already <c>needs_reauth</c> is left as it
    /// is, so repeated refusals never mint a second prompt.
    /// </summary>
    Task<Result> MarkRefreshRefusedAsync(
        Guid connectionId,
        string error,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Records a refresh attempt that failed for a reason saying nothing about the grant (5xx, 429, a timeout,
    /// a network error): stamps <c>LastErrorAt</c> so the refresher backs off, and never touches the failure
    /// count or the status.
    /// </summary>
    Task<Result> MarkTransientRefreshFailureAsync(
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

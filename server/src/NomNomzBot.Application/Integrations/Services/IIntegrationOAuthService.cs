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
using NomNomzBot.Application.Integrations.Dtos;

namespace NomNomzBot.Application.Integrations.Services;

/// <summary>
/// The generic, descriptor-driven OAuth connect flow for non-Twitch providers (integrations-oauth §3.1):
/// authorize → callback → token-exchange, then hand the tokens to identity-auth's
/// <c>IIntegrationTokenVault</c> (crypto-vaulted). It owns the OAuth dance with PKCE + signed single-use
/// state and is generic over <c>OAuthProviderDescriptor</c> — a new provider is a descriptor, not new code.
/// It never stores tokens itself.
/// </summary>
public interface IIntegrationOAuthService
{
    /// <summary>
    /// Builds the authorize URL (PKCE challenge + signed state bound to broadcaster/provider/scopeSet/
    /// returnUrl) and stashes the verifier + state single-use in the cache. Fails on unknown provider,
    /// invalid scope-set, or a disallowed tier-gated connect.
    /// </summary>
    Task<Result<OAuthStartDto>> StartConnectAsync(
        Guid broadcasterId,
        string provider,
        string scopeSetKey,
        string? returnUrl,
        Guid actingUserId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Handles the callback: validates+consumes state, exchanges code (+PKCE verifier) for tokens, reads the
    /// provider account identity, then persists via <c>IIntegrationTokenVault</c> (upsert + vault) and
    /// reconciles granted vs requested scopes. Fail-closed on state/PKCE/exchange failure (no partial
    /// connection persisted).
    /// </summary>
    Task<Result<OAuthCallbackResultDto>> HandleCallbackAsync(
        string provider,
        OAuthCallbackParams callbackParams,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Revokes the provider token where supported, then disconnects via the vault (soft-delete). Idempotent.
    /// </summary>
    Task<Result> DisconnectAsync(
        Guid broadcasterId,
        string provider,
        Guid actingUserId,
        CancellationToken cancellationToken = default
    );

    /// <summary>The integrations-screen read model: per provider, connected?, account, granted scope-sets, capabilities.</summary>
    Task<Result<IReadOnlyList<IntegrationStatusDto>>> GetStatusAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );
}

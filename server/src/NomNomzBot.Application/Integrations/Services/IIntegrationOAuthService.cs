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
    /// <para>
    /// <paramref name="publicOrigin"/> is the public <c>scheme://host</c> the request arrived on (the tunnel /
    /// domain the dashboard was served from, resolved by the API layer) — the <c>redirect_uri</c> is built from
    /// it and persisted in the state so the callback's token exchange reuses the exact same value (OAuth requires
    /// a byte-for-byte match). Spotify rejects loopback callbacks, so this must be the https public origin.
    /// </para>
    /// <para>
    /// <paramref name="shopDomain"/> is the shop name for a shop-scoped provider (Shopify) — required there,
    /// ignored elsewhere. Accepts the bare name or a pasted <c>name.myshopify.com</c>; it is sanitized and
    /// persisted in the state so the callback exchanges against the same shop.
    /// </para>
    /// </summary>
    Task<Result<OAuthStartDto>> StartConnectAsync(
        Guid broadcasterId,
        string provider,
        string scopeSetKey,
        string? returnUrl,
        Guid actingUserId,
        string publicOrigin,
        string? shopDomain = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Handles the callback: validates+consumes state, exchanges code (+PKCE verifier) for tokens, reads the
    /// provider account identity, then persists via <c>IIntegrationTokenVault</c> (upsert + vault) and
    /// reconciles granted vs requested scopes. Fail-closed on state/PKCE/exchange failure (no partial
    /// connection persisted). The token exchange's <c>redirect_uri</c> is the one persisted at connect-start, so
    /// it matches the authorize request exactly regardless of the callback request's host.
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

// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Identity.Services;

/// <summary>
/// CSRF-safe OAuth <c>state</c> for the Twitch authorize/callback round-trip (§5). The flow payload
/// (which flow, optional mobile redirect, optional channel id) is held <b>server-side</b> in the cache,
/// keyed by a single-use random nonce; only the opaque nonce travels through Twitch. The callback can
/// therefore only proceed for a nonce this server issued (defeats login/OAuth CSRF), and the payload
/// cannot be forged or tampered with (defeats the unsigned channel-bot state).
/// </summary>
public interface ITwitchOAuthStateService
{
    /// <summary>Stores <paramref name="state"/> under a fresh nonce and returns the nonce to use as the OAuth <c>state</c>.</summary>
    Task<string> IssueAsync(TwitchOAuthFlowState state, CancellationToken ct = default);

    /// <summary>
    /// Looks up and removes (single-use) the payload for <paramref name="stateNonce"/>. Returns null when the
    /// nonce is absent, already used, or expired — the callback must reject in that case.
    /// </summary>
    Task<TwitchOAuthFlowState?> ConsumeAsync(string? stateNonce, CancellationToken ct = default);
}

/// <summary>
/// The server-side OAuth flow payload. <paramref name="Flow"/> routes the single callback
/// (<c>user</c> / <c>bot</c> / <c>channel_bot</c>); <paramref name="RedirectUri"/> is the optional mobile
/// deep-link; <paramref name="ChannelId"/> is the tenant for the channel-bot flow; <paramref name="Client"/>
/// is the optional client class that started the flow (e.g. <c>web</c>) — the served-web dashboard navigates
/// the whole page to Twitch, so a JSON body can't reach it; the callback uses this to instead 302 back to the
/// served origin with the access token in the URL fragment + the refresh token in an HttpOnly cookie.
/// <para>
/// <paramref name="Provider"/> + <paramref name="CodeVerifier"/> carry a multi-platform auth-code+PKCE LOGIN
/// (platform-identity §10.3): the login-provider key (kick / twitter) the generic callback routes to, and the
/// PKCE verifier proving the same client that started the flow is finishing it. Both null for Twitch flows.
/// </para>
/// <para>
/// <paramref name="LinkUserId"/> marks an auth-code+PKCE flow as a LINK (platform-identity §4) rather than a
/// login: the callback attaches the proven identity to this already-authenticated user instead of minting a
/// session. Null for a plain login. (Device-grant links complete on an authenticated poll and carry the user in
/// the JWT, so they never need this.)
/// </para>
/// <para>
/// <paramref name="ReturnTo"/> is the served-web page (same-origin RELATIVE path, validated by the issuing
/// endpoint before it enters the state) the dashboard returns to after the round-trip — so an OAuth hop
/// started from /commands lands back on /commands, not the home page. Null lands on the origin root.
/// </para>
/// </summary>
public sealed record TwitchOAuthFlowState(
    string Flow,
    string? RedirectUri = null,
    string? ChannelId = null,
    string? Client = null,
    string? Provider = null,
    string? CodeVerifier = null,
    Guid? LinkUserId = null,
    string? ReturnTo = null
);

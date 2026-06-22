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
/// deep-link; <paramref name="ChannelId"/> is the tenant for the channel-bot flow.
/// </summary>
public sealed record TwitchOAuthFlowState(
    string Flow,
    string? RedirectUri = null,
    string? ChannelId = null
);

// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Discord;

/// <summary>
/// CSRF-safe OAuth <c>state</c> for the Discord bot-install authorize/callback round-trip (discord.md §5),
/// the Discord-flow analogue of <c>ITwitchOAuthStateService</c>. The flow payload (the tenant channel id plus
/// an optional desktop loopback <c>redirect_uri</c>) is held <b>server-side</b> in the cache, keyed by a
/// single-use random nonce; only the opaque nonce travels through Discord. The callback can therefore only
/// proceed for a nonce this server issued (defeats OAuth CSRF), and the channel id cannot be forged into the
/// query string (defeats a forged guild→channel binding).
/// </summary>
public interface IDiscordOAuthStateService
{
    /// <summary>Stores <paramref name="state"/> under a fresh nonce and returns the nonce to use as the OAuth <c>state</c>.</summary>
    Task<string> IssueAsync(DiscordOAuthFlowState state, CancellationToken ct = default);

    /// <summary>
    /// Looks up and removes (single-use) the payload for <paramref name="stateNonce"/>. Returns null when the
    /// nonce is absent, already used, or expired — the callback must reject in that case.
    /// </summary>
    Task<DiscordOAuthFlowState?> ConsumeAsync(string? stateNonce, CancellationToken ct = default);
}

/// <summary>
/// The server-side Discord OAuth flow payload. <paramref name="ChannelId"/> is the tenant the bot-install binds
/// to; <paramref name="RedirectUri"/> is the optional desktop loopback listener URL (RFC-8252) the KMP client
/// opened the flow with — when present, the callback redirects there with a success/error marker so the
/// loopback listener completes; when absent it falls back to the web frontend redirect.
/// </summary>
public sealed record DiscordOAuthFlowState(string ChannelId, string? RedirectUri = null);

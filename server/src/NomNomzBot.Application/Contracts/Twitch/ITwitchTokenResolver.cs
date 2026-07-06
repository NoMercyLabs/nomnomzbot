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

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// Resolves a usable, decrypted Helix bearer token for a call (twitch-helix.md §3.5), choosing
/// the bot/app token or the broadcaster's user token, and exposes scope state for pre-checks.
/// The hard invariant: the resolver yields a Twitch <c>string</c> identity for the limiter and
/// the bearer — Twitch never receives a tenant <see cref="Guid"/>.
/// </summary>
public interface ITwitchTokenResolver
{
    /// <summary>
    /// Returns the bot account token (service <c>twitch_bot</c>, no broadcaster). Used for
    /// read endpoints whose subject is not a specific tenant. Fails with <c>no_token</c> when absent.
    /// </summary>
    Task<Result<TwitchAccessContext>> GetBotTokenAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the broadcaster's user token (service <c>twitch</c>) for tenant <paramref name="broadcasterId"/>,
    /// falling back to the bot token when no user token exists. Fails with <c>no_token</c> when neither is present.
    /// </summary>
    Task<Result<TwitchAccessContext>> GetBroadcasterTokenAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Returns the logged-in operator's OWN Twitch user token (service <c>twitch</c>) — the connection whose
    /// Twitch account IS this user, independent of which tenant it is filed under (chat-client.md §3.1). Used to
    /// send/act AS the operator (a moderator in a channel they moderate), not the tenant broadcaster. Fails with
    /// <c>no_token</c> when the user has no Twitch identity or connection.
    /// </summary>
    Task<Result<TwitchAccessContext>> GetUserTokenAsync(
        Guid userId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Forces a single token refresh for the identity behind <paramref name="context"/> via the auth layer
    /// and returns the refreshed context. Called by the transport on a 401 (refresh-and-retry once).
    /// Fails when no refresh is possible (e.g. the app/bot token, or the refresh itself failed).
    /// </summary>
    Task<Result<TwitchAccessContext>> RefreshAsync(
        TwitchAccessContext context,
        CancellationToken ct = default
    );

    /// <summary>
    /// True if the connection backing the broadcaster's token has been granted <paramref name="scope"/>.
    /// Reads the granted scope set; a missing scope short-circuits a call with <c>missing_scope</c>.
    /// </summary>
    Task<bool> HasScopeAsync(Guid broadcasterId, string scope, CancellationToken ct = default);
}

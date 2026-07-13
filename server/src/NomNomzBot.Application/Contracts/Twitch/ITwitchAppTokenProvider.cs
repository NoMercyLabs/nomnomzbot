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
/// Mints and caches the platform's Twitch <em>app access token</em> (the <c>client_credentials</c> grant,
/// twitch-helix.md §3.5). This is the subject-agnostic token the chat send rides so Twitch awards the bot the
/// chatbot badge — a message sent on a plain user token never gets the badge, only one sent on an app token
/// (with the bot's <c>user:bot</c> grant + bot-is-mod / broadcaster <c>channel:bot</c>). The token is
/// process-wide and long-lived (~60 days), so it is minted once and shared; the implementation is a singleton.
/// </summary>
public interface ITwitchAppTokenProvider
{
    /// <summary>
    /// Returns a valid app access token, minting one on first use and re-minting when the cached one has
    /// expired. Fails with <c>no_token</c> when the platform app secret is not configured (a secret-less
    /// self-host on the shared public client) — the caller then degrades gracefully to the user-token send.
    /// </summary>
    Task<Result<string>> GetAppTokenAsync(CancellationToken ct = default);

    /// <summary>
    /// Drops the cached token so the next <see cref="GetAppTokenAsync"/> mints fresh. Called after a 401,
    /// where the cached token was rejected (revoked or lapsed ahead of its stated lifetime).
    /// </summary>
    void Invalidate();
}

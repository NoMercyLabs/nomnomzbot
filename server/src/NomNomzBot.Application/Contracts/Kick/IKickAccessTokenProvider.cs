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

namespace NomNomzBot.Application.Contracts.Kick;

/// <summary>
/// The ONE custody path for a Kick tenant's OAuth bearer (BUILD slice 3b-2c): resolves the tenant
/// channel to the streamer's vaulted Kick connection (the login flow vaults it keyed by the numeric
/// Kick account id) and returns a usable access token plus that numeric id — the
/// <c>broadcaster_user_id</c> every chat/moderation call needs. Transparently refreshes an expiring
/// token against id.kick.com; Kick is OAuth 2.1 and ROTATES the refresh token on every refresh grant,
/// so the new pair is re-vaulted each time. Null when the tenant is not a Kick channel, has no
/// connection, or the refresh fails (marked on the connection → reauth surface).
/// </summary>
public interface IKickAccessTokenProvider
{
    Task<KickAccess?> GetAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
}

/// <summary>
/// A usable Kick bearer + the numeric account id the public API keys on. <c>IsBotAccount</c> is true only
/// when the token belongs to a dedicated Kick bot-account connection (<c>Provider=kick_bot</c>, registered
/// via the channel-bot OAuth flow) — distinct from the streamer's own account; false when the tenant has
/// no such connection and the bot chats as the streamer's own account (the self-host fallback).
/// </summary>
public sealed record KickAccess(
    string AccessToken,
    long BroadcasterUserId,
    bool IsBotAccount = false
);

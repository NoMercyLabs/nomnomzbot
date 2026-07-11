// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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

/// <summary>A usable Kick bearer + the numeric account id the public API keys on.</summary>
public sealed record KickAccess(string AccessToken, long BroadcasterUserId);

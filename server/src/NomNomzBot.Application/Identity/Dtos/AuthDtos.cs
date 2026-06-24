// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Identity.Dtos;

/// <summary>Authentication result containing tokens and user info.</summary>
public sealed record AuthResultDto(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    UserDto User
);

/// <summary>OAuth callback data received from the Twitch redirect.</summary>
public sealed record OAuthCallbackDto
{
    public required string Code { get; init; }
    public string? State { get; init; }

    /// <summary>
    /// Custom redirect URI used by the client (e.g. <c>nomnomzbot://callback</c> for mobile).
    /// When provided, Twitch token exchange uses this instead of the server's configured URI.
    /// </summary>
    public string? RedirectUri { get; init; }
}

/// <summary>
/// Token refresh request. The refresh token is optional in the body: the served-web dashboard sends none and
/// relies on the HttpOnly cookie the browser attaches automatically, while native clients send the token they
/// hold in their own vault.
/// </summary>
public sealed record RefreshTokenRequest(string? RefreshToken);

/// <summary>The request fingerprint captured for a login session (identity-auth §4).</summary>
public sealed record AuthContextDto(string ClientType, string? IpAddress, string? UserAgent);

/// <summary>
/// The tokens issued for a session (identity-auth §4): the access JWT plus the RAW refresh token (returned
/// once — only its hash is persisted), with both expiries and the owning session id.
/// </summary>
public sealed record SessionTokensDto(
    string AccessToken,
    string RawRefreshToken,
    DateTime AccessExpiresAt,
    DateTime RefreshExpiresAt,
    Guid SessionId
);

/// <summary>Read model of an auth session (identity-auth §4).</summary>
public sealed record AuthSessionDto(
    Guid Id,
    Guid UserId,
    Guid? BroadcasterId,
    string ClientType,
    DateTime LastSeenAt,
    DateTime ExpiresAt,
    bool IsRevoked
);

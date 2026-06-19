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
    /// Custom redirect URI used by the client (e.g. <c>nomercybot://callback</c> for mobile).
    /// When provided, Twitch token exchange uses this instead of the server's configured URI.
    /// </summary>
    public string? RedirectUri { get; init; }
}

/// <summary>Token refresh request.</summary>
public sealed record RefreshTokenRequest(string RefreshToken);

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

namespace NomNomzBot.Application.Abstractions.Auth;

public interface ITwitchAuthService
{
    Task<TokenResult?> ExchangeCodeAsync(
        string code,
        string redirectUri,
        CancellationToken ct = default
    );

    // broadcasterId is the tenant (channel) Guid; null = the platform/shared-bot row (Service.BroadcasterId null).
    Task<TokenResult?> RefreshTokenAsync(
        Guid? broadcasterId,
        string serviceName,
        CancellationToken ct = default
    );

    // Refreshes exactly the one vaulted connection with this id. The only safe refresh for a personal
    // (no channel) Twitch grant, because (null, twitch) names many users' rows.
    Task<TokenResult?> RefreshConnectionAsync(Guid connectionId, CancellationToken ct = default);
    Task RefreshExpiringTokensAsync(CancellationToken ct = default);
    Task RevokeTokenAsync(Guid? broadcasterId, string serviceName, CancellationToken ct = default);
}

public record TokenResult(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    string[] Scopes
);

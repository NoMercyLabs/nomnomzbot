// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
    Task RefreshExpiringTokensAsync(CancellationToken ct = default);
    Task RevokeTokenAsync(Guid? broadcasterId, string serviceName, CancellationToken ct = default);
}

public record TokenResult(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    string[] Scopes
);

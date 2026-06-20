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

/// <summary>Upsert request for an <c>IntegrationConnection</c> (identity-auth §4). Carries no secrets.</summary>
public sealed record UpsertConnectionDto(
    Guid? BroadcasterId,
    string Provider,
    string? ProviderAccountId,
    string? ProviderAccountName,
    IReadOnlyList<string> Scopes,
    string? ClientId,
    bool IsByok,
    Guid? ConnectedByUserId,
    string? SettingsJson
);

/// <summary>The raw secrets to vault for a connection (identity-auth §4). Plaintext in transit only.</summary>
public sealed record StoreTokensDto(
    string AccessToken,
    string? RefreshToken,
    string? AppToken,
    DateTime? AccessExpiresAt
);

/// <summary>A decrypted token returned from the vault for use on an outbound provider call (identity-auth §4).</summary>
public sealed record DecryptedTokenDto(
    string Value,
    string TokenType,
    DateTime? ExpiresAt,
    bool IsExpired
);

/// <summary>Read model of a connection (identity-auth §4) — never includes ciphertext.</summary>
public sealed record IntegrationConnectionDto(
    Guid Id,
    Guid? BroadcasterId,
    string Provider,
    string? ProviderAccountId,
    string? ProviderAccountName,
    string Status,
    IReadOnlyList<string> Scopes,
    bool IsByok,
    DateTime? ConnectedAt,
    DateTime? LastRefreshedAt,
    int ConsecutiveFailureCount
);

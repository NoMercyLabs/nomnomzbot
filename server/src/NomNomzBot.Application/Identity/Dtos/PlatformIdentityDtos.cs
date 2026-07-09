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

/// <summary>
/// One external identity linked to the caller's account (platform-identity §4). Never carries tokens — those
/// live in the vault. Returned by <c>GET auth/identities</c>, primary first.
/// </summary>
public sealed record UserIdentityDto(
    string Provider,
    string ProviderUserId,
    string ProviderUsername,
    string? ProviderDisplayName,
    string? ProviderAvatarUrl,
    bool IsPrimary,
    DateTime LinkedAt,
    DateTime? LastLoginAt
);

/// <summary>
/// A login provider the client may offer on the login screen (platform-identity §4). <see cref="Enabled"/> is
/// the descriptor being registered AND its feature flag resolving true for this deployment — the login screen
/// renders only enabled providers, so there are never dead buttons. <see cref="Flows"/> are wire tokens
/// (<c>device_code</c> | <c>auth_code_pkce</c> | <c>auth_code</c>) telling the client which handshake to run.
/// </summary>
public sealed record LoginProviderDto(
    string Key,
    string DisplayName,
    IReadOnlyList<string> Flows,
    bool Enabled
);

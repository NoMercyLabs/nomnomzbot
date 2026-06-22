// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// First-admin bootstrap decision (§12). A self-hoster sets <c>App:InitialAdminTwitchId</c> to their
/// Twitch user id; on login the matching account is promoted to platform principal so no raw SQL is
/// needed to create the first admin. Pure, so the promotion condition — opt-in, exact-match, and
/// idempotent (never re-promotes) — is unit-tested without standing up the login pipeline.
/// </summary>
internal static class AdminBootstrap
{
    public static bool ShouldPromote(
        bool isAlreadyPlatformPrincipal,
        string? configuredAdminTwitchId,
        string userTwitchId
    ) =>
        !isAlreadyPlatformPrincipal
        && !string.IsNullOrWhiteSpace(configuredAdminTwitchId)
        && configuredAdminTwitchId == userTwitchId;
}

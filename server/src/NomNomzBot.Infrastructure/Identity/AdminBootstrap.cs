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
/// First-admin bootstrap decision (§12). Two ways an account becomes the platform principal on login,
/// without raw SQL and idempotently (an existing principal is never re-promoted):
/// <list type="bullet">
///   <item>Explicit opt-in (any deployment): the account whose Twitch id exactly matches the configured
///   <c>App:InitialAdminTwitchId</c>.</item>
///   <item>Self-host convention: the owner IS the admin, so the FIRST account to onboard — when no platform
///   principal exists yet — is auto-promoted with no configuration. SaaS never auto-promotes (its admins are
///   pre-provisioned platform staff).</item>
/// </list>
/// Pure, so every branch is unit-tested without standing up the login pipeline.
/// </summary>
internal static class AdminBootstrap
{
    public static bool ShouldPromote(
        bool isAlreadyPlatformPrincipal,
        string? configuredAdminTwitchId,
        string userTwitchId,
        bool isSelfHost,
        bool anyPlatformPrincipalExists
    )
    {
        if (isAlreadyPlatformPrincipal)
            return false;

        bool matchesConfiguredAdmin =
            !string.IsNullOrWhiteSpace(configuredAdminTwitchId)
            && configuredAdminTwitchId == userTwitchId;

        bool isFirstSelfHostOwner = isSelfHost && !anyPlatformPrincipalExists;

        return matchesConfiguredAdmin || isFirstSelfHostOwner;
    }
}

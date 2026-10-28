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

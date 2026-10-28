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

using Microsoft.Extensions.Configuration;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// The one check for "is this the bot identity this deployment is configured to auto-grant moderator". An id pin
/// (<c>Twitch:BotUserId</c>) wins because it survives a Twitch rename; the username (<c>Twitch:BotUsername</c>) is
/// the fallback. With neither configured nothing is auto-granted: a deployment that has not named its bot has not
/// consented to hand anyone moderator (2026-09-04 incident).
/// </summary>
public static class SaasBotPin
{
    public static bool IsConfigured(
        IConfiguration configuration,
        string botUserId,
        string botUsername
    )
    {
        string? expectedUserId = configuration["Twitch:BotUserId"];
        if (!string.IsNullOrWhiteSpace(expectedUserId))
            return string.Equals(botUserId, expectedUserId.Trim(), StringComparison.Ordinal);

        string? expectedUsername = configuration["Twitch:BotUsername"];
        if (!string.IsNullOrWhiteSpace(expectedUsername))
            return string.Equals(
                botUsername,
                expectedUsername.Trim(),
                StringComparison.OrdinalIgnoreCase
            );

        return false;
    }
}

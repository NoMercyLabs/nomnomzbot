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

namespace NomNomzBot.Infrastructure.Discord.EventHandlers;

/// <summary>Turns a subscription tier from the platform into the words a Discord post shows.</summary>
internal static class DiscordTierLabel
{
    /// <summary>Twitch sends "1000", "2000" and "3000"; any other platform name (a YouTube member level) stays as it is.</summary>
    public static string Of(string tier) =>
        tier switch
        {
            "1000" => "Tier 1",
            "2000" => "Tier 2",
            "3000" => "Tier 3",
            _ => tier,
        };
}

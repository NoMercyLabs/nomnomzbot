// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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

// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Bot;

/// <summary>The channel's bot settings (prefix, personality, language), its dashboard link and its timers.</summary>
internal sealed class BotChannelHelpers()
    : BotHelperGroup(
        BotRow.Memory("bot.prefix", "!", call => call.Channel?.CommandPrefix),
        BotRow.Memory(
            "bot.personality",
            "Informative",
            call => call.Channel is { } channel ? StreamText.Capitalized(channel.Personality) : null
        ),
        BotRow.Memory(
            "bot.language",
            "en",
            call => call.Channel is { } channel ? channel.Language ?? "en" : null
        ),
        BotRow.Read(
            "bot.dashboard",
            "https://nomnomz.bot/app/u/samplechannel",
            BotSources.Origin,
            DashboardAsync
        ),
        BotRow.Read("bot.timers.count", "5", BotSources.Timers, TimersAsync)
    )
{
    /// <summary>The dashboard of the channel on the operator's public origin; nothing while none is configured.</summary>
    private static async Task<string?> DashboardAsync(BotCall call)
    {
        if (call.Channel is not { } channel)
            return null;

        return await call.Data.OriginAsync() is { } origin
            ? BotText.DashboardLink(origin, channel.ChannelName)
            : BotText.Nothing;
    }

    /// <summary>How many timers of the channel are enabled.</summary>
    private static async Task<string?> TimersAsync(BotCall call)
    {
        if (call.Channel is null)
            return null;

        return await call.Data.EnabledTimersAsync() is { } count
            ? PeopleText.Count(count, call.Culture)
            : BotText.Nothing;
    }
}

// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Bot;

/// <summary>How long the bot process runs, when it started, and how long ago the channel was loaded.</summary>
internal sealed class BotUptimeHelpers()
    : BotHelperGroup(
        BotRow.Memory("bot.uptime", "3d 4h", call => BotText.Span(Running(call))),
        BotRow.Memory(
            "bot.uptime.long",
            "3 days, 4 hours",
            call => StreamText.Words(Running(call), call.Culture)
        ),
        BotRow.Memory(
            "bot.startedAt",
            "2026-10-23 12:30",
            call => BotText.Local(call.Data.Process.StartedAt, call.Scope.Timezone)
        ),
        BotRow.Memory(
            "bot.lastRestart.relative",
            "3 days ago",
            call => StreamText.Ago(call.Data.Process.StartedAt, call.Now, call.Culture)
        ),
        BotRow.Memory(
            "bot.channel.loadedAgo",
            "2h 20m",
            call =>
                call.Channel is { } channel
                    ? BotText.Span(BotText.Since(channel.LoadedAt, call.Now))
                    : null
        )
    )
{
    private static TimeSpan Running(BotCall call) =>
        BotText.Since(call.Data.Process.StartedAt, call.Now);
}

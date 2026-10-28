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

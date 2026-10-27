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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Bot;

/// <summary>
/// The command lists and their count. A list needs the channel and the built-in service; when either is not
/// there, it prints nothing instead of a list with a gap.
/// </summary>
internal sealed class BotCommandHelpers()
    : BotHelperGroup(
        BotRow.Read(
            "bot.commands.list",
            "!hug !lurk !so !uptime !sr",
            BotSources.Builtins,
            call => ListAsync(call, BotCommands.All)
        ),
        BotRow.Memory(
            "bot.commands.list.custom",
            "!hug !lurk",
            call =>
                call.Channel is { } channel ? BotCommands.Join(BotCommands.Custom(channel)) : null
        ),
        BotRow.Read(
            "bot.commands.list.builtin",
            "!so !uptime !sr",
            BotSources.Builtins,
            call => ListAsync(call, (_, builtins) => builtins)
        ),
        BotRow.Read(
            "bot.commands.list.forUser",
            "!hug !lurk !sr",
            BotSources.Builtins,
            call =>
                ListAsync(
                    call,
                    (custom, builtins) =>
                        BotCommands.ForRole(
                            BotCommands.All(custom, builtins),
                            call.Seed("user.role")
                        )
                )
        ),
        BotRow.Read("bot.commands.count", "5", BotSources.Builtins, CountAsync)
    )
{
    private static async Task<string?> ListAsync(
        BotCall call,
        Func<IReadOnlyList<BotCommand>, IReadOnlyList<BotCommand>, IReadOnlyList<BotCommand>> pick
    ) =>
        await CommandsAsync(call, pick) is { } commands
            ? BotCommands.Join(commands)
            : BotText.Nothing;

    private static async Task<string?> CountAsync(BotCall call)
    {
        if (call.Channel is null)
            return null;

        IReadOnlyList<BotCommand>? commands = await CommandsAsync(call, BotCommands.All);
        return commands is null ? BotText.Nothing : PeopleText.Count(commands.Count, call.Culture);
    }

    private static async Task<IReadOnlyList<BotCommand>?> CommandsAsync(
        BotCall call,
        Func<IReadOnlyList<BotCommand>, IReadOnlyList<BotCommand>, IReadOnlyList<BotCommand>> pick
    ) =>
        call.Channel is { } channel && await call.Data.BuiltinsAsync() is { } builtins
            ? pick(BotCommands.Custom(channel), builtins)
            : null;
}

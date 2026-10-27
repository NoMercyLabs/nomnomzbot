// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Bot;

public sealed class BotHelpersTableTests
{
    // The clock is 2026-10-26T14:30Z in Europe/Amsterdam. The bot started 3 days 4 hours earlier (2026-10-23
    // 10:30Z, which is 12:30 local: summer time still ran on that day). The channel loaded 2 h 20 m ago.
    // The catalog cell shows 10:30 for bot.startedAt (the UTC time); the helper speaks the channel zone.
    // Commands: !hug and !lurk (custom), !so and !uptime (moderators only here) and !sr (subscribers).
    public static TheoryData<string, string> Rows =>
        new()
        {
            { "{bot.name}", "SampleBot" },
            { "{bot.mention}", "@SampleBot" },
            { "{bot.version}", "1.42.0" },
            { "{bot.uptime}", "3d 4h" },
            { "{bot.uptime.long}", "3 days, 4 hours" },
            { "{bot.startedAt}", "2026-10-23 12:30" },
            { "{bot.lastRestart.relative}", "3 days ago" },
            { "{bot.channel.loadedAgo}", "2h 20m" },
            { "{if:{bot.isMod}|I can moderate|please /mod me}", "I can moderate" },
            { "{bot.prefix}", "!" },
            { "{bot.commands.list}", "!hug !lurk !so !uptime !sr" },
            { "{bot.commands.list.custom}", "!hug !lurk" },
            { "{bot.commands.list.builtin}", "!so !uptime !sr" },
            { "{bot.commands.list.forUser}", "!hug !lurk !sr" },
            { "{bot.commands.count}", "5" },
            { "{bot.timers.count}", "5" },
            { "{bot.personality}", "Informative" },
            { "{bot.language}", "en" },
            { "{bot.dashboard}", "https://nomnomz.bot/app/u/samplechannel" },
            { "{bot.sourceCode}", "https://github.com/NoMercyLabs/nomnomzbot" },
            { "{bot.license}", "AGPL-3.0-or-later" },
        };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Every_row_renders_the_exact_output_of_the_catalog_fixture(
        string template,
        string expected
    )
    {
        await using BotHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(template);

        rendered.Should().Be(expected);
    }
}

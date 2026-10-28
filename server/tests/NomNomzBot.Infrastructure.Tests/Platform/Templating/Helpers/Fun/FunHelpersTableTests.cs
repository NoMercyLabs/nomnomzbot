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

using FluentAssertions;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Fun;

/// <summary>
/// Catalog section 7.18: every Example renders to exactly its Output cell through the real parser, registry and
/// resolver, against the section 5 fixture (clock 2026-10-26T14:30:00Z, user ViewerOne, target ViewerTwo). A random
/// row runs with <c>new Random(1)</c> and asserts the value that seed gives.
/// </summary>
public sealed class FunHelpersTableTests
{
    public static TheoryData<string, string> Rows =>
        new()
        {
            // Text art and other forms with one fixed output.
            { "{fun.unflip}", "┬─┬ノ( º _ ºノ)" },
            { "{fun.shrug}", "¯\\_(ツ)_/¯" },
            { "{fun.lenny}", "( ͡° ͜ʖ ͡°)" },
            { "{fun.flip:table}", "(╯°□°)╯︵ ǝlqɐʇ" },
            { "{fun.countdown:3}", "3… 2… 1…" },
            { "{fun.countdown:1}", "1…" },
            { "{fun.meter:60}", "██████░░░░ 60%" },
            // Seeded rows: the value Random(1) gives.
            { "{fun.coin}", "heads" },
            { "{fun.coin.emoji}", "🪙 heads" },
            { "{fun.dice}", "2" },
            { "{fun.dice:20}", "5" },
            { "{fun.dice:2d6}", "3" },
            { "{fun.dice.detail:2d6}", "2 + 1 = 3" },
            { "{fun.dice.emoji}", "⚁" },
            { "{fun.dice.emoji:3}", "⚁ ⚀ ⚂" },
            { "{fun.rps}", "🪨 rock" },
            { "{fun.rps.vs:rock}", "rock — a draw" },
            { "{fun.card}", "K♠" },
            { "{fun.slots}", "🍋 🍒 🔔" },
            { "{fun.roulette}", "click" },
            { "{fun.random.emoji}", "🔥" },
            { "{fun.random.animal}", "🐶 dog" },
            { "{fun.random.food}", "🌮 taco" },
            { "{fun.random.color.name}", "blue" },
            // Hashed rows: stable for the pair or thing and the day (2026-10-26).
            { "{fun.love:{user.display}|{target.display}}", "22%" },
            { "{fun.rate:pizza}", "8/10" },
            { "{fun.percent:luck}", "8%" },
            { "{fun.ship:{user.display}|{target.display}}", "ViewerOne + ViewerTwo = ViewTwo" },
            // Bad arguments print the token as written.
            { "{fun.dice:0}", "{fun.dice:0}" },
            { "{fun.dice:abc}", "{fun.dice:abc}" },
            { "{fun.dice:1d1}", "{fun.dice:1d1}" },
            { "{fun.dice:101d6}", "{fun.dice:101d6}" },
            { "{fun.dice:2d6|3}", "{fun.dice:2d6|3}" },
            { "{fun.dice.detail:xd6}", "{fun.dice.detail:xd6}" },
            { "{fun.dice.emoji:0}", "{fun.dice.emoji:0}" },
            { "{fun.coin:extra}", "{fun.coin:extra}" },
            { "{fun.countdown:0}", "{fun.countdown:0}" },
            { "{fun.countdown:21}", "{fun.countdown:21}" },
            { "{fun.countdown}", "{fun.countdown}" },
            { "{fun.love:solo}", "{fun.love:solo}" },
            { "{fun.ship:a|}", "{fun.ship:a|}" },
            { "{fun.rate}", "{fun.rate}" },
            { "{fun.rps.vs:lizard}", "{fun.rps.vs:lizard}" },
            { "{fun.flip}", "{fun.flip}" },
            { "{fun.size}", "{fun.size}" },
        };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task The_example_renders_to_exactly_the_output_cell(
        string template,
        string expected
    )
    {
        await using FunHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(template);

        rendered.Should().Be(expected);
    }
}

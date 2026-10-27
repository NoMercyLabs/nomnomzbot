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
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Logic;
using NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Text;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Logic;

/// <summary>The random rows with a seeded <see cref="Random"/> (seed 1): the exact values the seed gives.</summary>
public sealed class LogicHelpersSeededTests
{
    [Fact]
    public async Task The_random_rows_render_the_values_seed_1_gives()
    {
        string rendered = await RenderAsync(
            "{random.number:100}|{random.number:10|50|10}|{random.decimal:0|1|2}|{random.percent}|{random.bool}|{random.letter}|{random.color}"
        );

        rendered.Should().Be("25|10|0.47|77%|true|L|#5AA53B");
    }

    [Theory]
    [InlineData("{chance:50|yes|no}", "yes")]
    [InlineData("{chance:20|yes|no}", "no")]
    [InlineData("{pick.weighted:1=rare|9=common}", "common")]
    [InlineData("{pick.n:2|a|b|c}", "b, c")]
    [InlineData("{pick.shuffle:a|b|c|d|e}", "d, c, e, a, b")]
    public async Task A_seeded_row_renders_the_value_seed_1_gives(string template, string expected)
    {
        string rendered = await RenderAsync(template);

        rendered.Should().Be(expected);
    }

    [Fact]
    public async Task The_same_seed_gives_the_same_text_and_a_different_seed_does_not()
    {
        const string Template = "{pick.shuffle:a|b|c|d|e|f|g|h}";

        string first = await RenderAsync(Template, new Random(1));
        string second = await RenderAsync(Template, new Random(1));
        string other = await RenderAsync(Template, new Random(2));

        second.Should().Be(first);
        other.Should().NotBe(first);
    }

    [Fact]
    public async Task A_number_draw_stays_on_its_step_and_inside_its_range()
    {
        Random random = new(1);
        HashSet<string> seen = [];
        for (int i = 0; i < 200; i++)
            seen.Add(await RenderAsync("{random.number:10|50|10}", random));

        seen.Should().BeEquivalentTo("10", "20", "30", "40", "50");
    }

    [Fact]
    public async Task A_pick_n_gives_different_items_of_the_list()
    {
        Random random = new(1);
        for (int i = 0; i < 50; i++)
        {
            string[] items = (await RenderAsync("{pick.n:3|a|b|c|d|e}", random)).Split(", ");

            items.Should().OnlyHaveUniqueItems().And.HaveCount(3);
            items.Should().OnlyContain(item => "abcde".Contains(item));
        }
    }

    private static Task<string> RenderAsync(string template) =>
        RenderAsync(template, new Random(1));

    private static async Task<string> RenderAsync(string template, Random random)
    {
        TemplateHelperRegistryAggregate registry = new([
            new IfTemplateHelpers(),
            new ChanceTemplateHelpers(random),
            new PickTemplateHelpers(random),
            new RandomTemplateHelpers(random),
        ]);
        return await TextHelpersTableTests
            .BuildResolver(registry)
            .ResolveAsync(template, LogicHelpersTableTests.Fixture, broadcasterId: null);
    }
}

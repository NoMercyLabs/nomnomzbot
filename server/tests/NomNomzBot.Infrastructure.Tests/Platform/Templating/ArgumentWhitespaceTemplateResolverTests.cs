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
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Text;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

/// <summary>
/// Catalog section 3, Quoting. Stoney, 2026-10-30: "you should not ignore the whitespace when resolving the
/// string template". A text argument reaches its helper exactly as written. An argument a helper parses (a
/// count, a number, a keyword, a name) ignores the spaces around it. Every case renders through the real
/// parser, registry and resolver and asserts the exact string.
/// </summary>
public sealed class ArgumentWhitespaceTemplateResolverTests
{
    private static readonly Dictionary<string, string> Seeds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["user"] = "ViewerOne",
    };

    [Theory]
    [InlineData("{text.repeat:3|ha }", "ha ha ha ")]
    [InlineData("{text.repeat:3| ha}", " ha ha ha")]
    [InlineData("{text.repeat:3|ha| - }", "ha - ha - ha")]
    [InlineData("{text.repeat:3|ha|}", "hahaha")]
    [InlineData("{text.wrap: hi |[|]}", "[ hi ]")]
    [InlineData("{text.wrap:hi|[ | ]}", "[ hi ]")]
    [InlineData("{text.pad.left:7|3| }", "  7")]
    [InlineData("{text.replace:a-b|-| + }", "a + b")]
    [InlineData("{text.spaced:hype| . }", "h . y . p . e")]
    [InlineData(@"{text.repeat:2|\s}", "  ")]
    public async Task A_text_argument_keeps_its_spaces_as_written(string template, string expected)
    {
        string rendered = await RenderAsync(template);

        rendered.Should().Be(expected);
    }

    [Theory]
    [InlineData(@"{text.repeat:3|rofl\s}", "rofl rofl rofl ")]
    [InlineData(@"{text.repeat:2|a\nb}", "a\nba\nb")]
    [InlineData(@"{text.upper:hello\sworld}", "HELLO WORLD")]
    [InlineData(@"{text.wrap:hi|[\s|\s]}", "[ hi ]")]
    [InlineData(@"{text.repeat:2|a\\sb}", @"a\sba\sb")]
    [InlineData(@"{if:true|yes\sthere|no}", "yes there")]
    [InlineData(@"{default:|none\sfound}", "none found")]
    [InlineData(@"{switch:b|a=x|b=two\swords}", "two words")]
    public async Task The_space_and_new_line_escapes_work_in_every_text_argument(
        string template,
        string expected
    )
    {
        string rendered = await RenderAsync(template);

        rendered.Should().Be(expected);
    }

    [Theory]
    [InlineData("{text.repeat: 3 |ha}", "hahaha")]
    [InlineData("{text.pad.left:7| 3 |0}", "007")]
    [InlineData("{text.cut:hello world| max = 8 |end=...}", "hello...")]
    [InlineData("{text.cut:hello world| max=8 | end=...}", "hello...")]
    [InlineData("{math.add: 2 | 3 }", "5")]
    [InlineData("{num.round: 2.567 | 1 }", "2.6")]
    [InlineData("{text.word: 2 |big world}", "world")]
    public async Task A_parsed_argument_ignores_the_spaces_around_it(
        string template,
        string expected
    )
    {
        string rendered = await RenderAsync(template);

        rendered.Should().Be(expected);
    }

    [Theory]
    [InlineData("{if:true| yes |no}", " yes ")]
    [InlineData("{if: true | yes | no }", " yes ")]
    [InlineData("{if:false| yes | no }", " no ")]
    [InlineData("{if:false| yes }", "")]
    [InlineData("{if.not:false| yes |no}", " yes ")]
    [InlineData("{default:| fallback }", " fallback ")]
    [InlineData("{default: x | fallback }", " x ")]
    [InlineData("{switch: b |a=x|b= chosen |d}", " chosen ")]
    [InlineData("{switch:z|a=x|b=y| other }", " other ")]
    [InlineData("{chance:100| sure |never}", " sure ")]
    [InlineData("{coalesce:| second }", " second ")]
    public async Task A_branch_output_keeps_its_spaces_and_a_condition_ignores_them(
        string template,
        string expected
    )
    {
        string rendered = await RenderAsync(template);

        rendered.Should().Be(expected);
    }

    [Theory]
    [InlineData("{set:greeting= hi there }[{var.greeting}]", "[ hi there ]")]
    [InlineData("{set: greeting =hi}[{var.greeting}]", "[hi]")]
    [InlineData("{pick: a }", " a ")]
    [InlineData("{pick.n: 1 | a }", " a ")]
    [InlineData("{pick.weighted: 1 = a }", " a ")]
    [InlineData("{pick.inline: a ; a }", " a ")]
    [InlineData("{random.decimal: 1 | 1 | 2 }", "1.00")]
    [InlineData("{random.number: 5 | 5 }", "5")]
    public async Task Variables_and_picks_keep_the_text_they_print_and_ignore_the_spaces_of_what_they_read(
        string template,
        string expected
    )
    {
        string rendered = await RenderAsync(template);

        rendered.Should().Be(expected);
    }

    [Theory]
    [InlineData("{fun.love: ViewerOne | ViewerTwo }", "{fun.love:ViewerOne|ViewerTwo}")]
    [InlineData("{fun.ship: ViewerOne | ViewerTwo }", "{fun.ship:ViewerOne|ViewerTwo}")]
    public async Task A_name_or_keyword_argument_reads_the_same_with_or_without_spaces(
        string spaced,
        string tight
    )
    {
        string withSpaces = await RenderAsync(spaced);
        string without = await RenderAsync(tight);

        without.Should().NotContain("{", "the helper must answer the tight form");
        withSpaces.Should().Be(without);
    }

    private static async Task<string> RenderAsync(string template)
    {
        await using ServiceProvider services = new ServiceCollection()
            .AddTemplateHelpers(typeof(TemplateResolver).Assembly)
            .BuildServiceProvider();
        return await TextHelpersTableTests
            .BuildResolver(services.GetRequiredService<ITemplateHelperRegistry>())
            .ResolveAsync(template, Seeds, broadcasterId: null);
    }
}

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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Text;

/// <summary>
/// Catalog section 7.3, <c>text.upper</c> to <c>text.chars.shuffle</c>: every Example renders to exactly its
/// Output cell through the real parser, registry and resolver.
/// </summary>
public sealed class TextHelpersTableTests
{
    private static readonly Dictionary<string, string> Seeds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["user"] = "ViewerOne",
    };

    public static TheoryData<string, string> Rows =>
        new()
        {
            { "{text.upper:hello}", "HELLO" },
            { "{text.lower:HeLLo}", "hello" },
            { "{text.title:big world}", "Big World" },
            { "{text.sentence:hello WORLD. bye}", "Hello world. Bye" },
            { "{text.capitalize:hello world}", "Hello world" },
            { "{text.decapitalize:Hello}", "hello" },
            { "{text.swapcase:Hello}", "hELLO" },
            { "{text.spaced:hype}", "h y p e" },
            { "{text.spaced:hype|.}", "h.y.p.e" },
            { "{text.alternating:hello}", "HeLlO" },
            { "{text.mocking:hello}", "hElLo" },
            { "{text.spongecase:hello world}", "hElLo WoRlD" },
            { "{text.reverse:abc}", "cba" },
            { "{text.reverse.words:hello big world}", "world big hello" },
            { "{text.trim:  hi  }", "hi" },
            { "{text.squish:a    b  c}", "a b c" },
            { "{text.truncate:5|hello world}", "hello…" },
            { "{text.cut:hello world|max=8|end=...}", "hello..." },
            { "{text.cut.words:hello big world|2}", "hello big…" },
            { "{text.pad.left:7|3|0}", "007" },
            { "{text.pad.right:ab|4|.}", "ab.." },
            { "{text.center:hi|6|*}", "**hi**" },
            { "{text.repeat:3|ha}", "hahaha" },
            { "{text.repeat:3|ha| }", "ha ha ha" },
            { "{text.replace:hello world|world|chat}", "hello chat" },
            { "{text.replace.first:a a a|a|b}", "b a a" },
            { "{text.replace.icase:Hello|hello|bye}", "bye" },
            { "{text.remove:hello world|o}", "hell wrld" },
            { "{text.wrap:hi|[|]}", "[hi]" },
            { "{text.quote:hi}", "\"hi\"" },
            { "{text.paren:hi}", "(hi)" },
            { "{text.lines.wrap:aaa bbb ccc|7}", "aaa bbb / ccc" },
            { "{text.words.count:hello big world}", "3" },
            { "{text.chars.count:hello}", "5" },
            { "{text.letters.count:a1 b2}", "2" },
            { "{text.lines.count:a\nb}", "2" },
            { @"{text.lines.count:a\nb}", "2" },
            { "{text.count:banana|a}", "3" },
            { "{text.word.first:hello big world}", "hello" },
            { "{text.word.last:hello big world}", "world" },
            { "{text.word:2|hello big world}", "big" },
            { "{text.words.first:2|hello big world}", "hello big" },
            { "{text.words.last:2|hello big world}", "big world" },
            { "{text.words.from:2|hello big world}", "big world" },
            { "{text.words.range:1|2|hello big world}", "hello big" },
            { "{text.words.sort:c a b}", "a b c" },
            { "{text.words.unique:a b a}", "a b" },
            { "{text.word.longest:hi hello yo}", "hello" },
            // Edge cases the catalog implies: graphemes, escapes, nesting, bad arguments.
            { "{text.chars.count:👍a}", "2" },
            { "{text.reverse:a👍b}", "b👍a" },
            { @"{text.pad.left:5|7|\s}", "      5" },
            { "{text.upper:{user}}", "VIEWERONE" },
            { "{text.truncate:x|hi}", "{text.truncate:x|hi}" },
            { "{text.word:0|hello big}", "{text.word:0|hello big}" },
            { "{text.word:9|hello big}", "" },
            { "{text.cut:hello}", "{text.cut:hello}" },
            { "{text.replace:abc||x}", "{text.replace:abc||x}" },
            { "{text.repeat:5000|ha}", "{text.repeat:5000|ha}" },
        };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task The_example_renders_to_exactly_the_output_cell(
        string template,
        string expected
    )
    {
        string rendered = await RenderAsync(template);

        rendered.Should().Be(expected);
    }

    [Theory]
    [InlineData("{text.words.shuffle:a b c}", "a|b|c", " ")]
    [InlineData("{text.chars.shuffle:abc}", "a|b|c", "")]
    public async Task A_shuffle_row_renders_a_permutation_of_its_input(
        string template,
        string pieces,
        string separator
    )
    {
        string rendered = await RenderAsync(template);

        string[] actual =
            separator == "" ? [.. rendered.Select(c => c.ToString())] : rendered.Split(separator);
        actual.Order().Should().Equal(pieces.Split('|'));
    }

    [Fact]
    public async Task The_random_word_row_renders_one_of_the_words()
    {
        string rendered = await RenderAsync("{text.word.random:a b c}");

        new[] { "a", "b", "c" }.Should().Contain(rendered);
    }

    private static async Task<string> RenderAsync(string template)
    {
        await using ServiceProvider services = new ServiceCollection()
            .AddTemplateHelpers(typeof(TemplateResolver).Assembly)
            .BuildServiceProvider();
        return await BuildResolver(services.GetRequiredService<ITemplateHelperRegistry>())
            .ResolveAsync(template, Seeds, broadcasterId: null);
    }

    internal static TemplateResolver BuildResolver(ITemplateHelperRegistry registry) =>
        new(
            new ServiceCollection()
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IChannelRegistry>(),
            NullLogger<TemplateResolver>.Instance,
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 26, 14, 30, 0, TimeSpan.Zero)),
            registry
        );
}

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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Infrastructure.Platform.Templating;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Text;

/// <summary>
/// Catalog section 7.3, <c>text.first</c> to <c>text.escape.chat</c>: every Example renders to exactly its
/// Output cell through the real parser, registry and resolver.
/// </summary>
public sealed class TextHelpersTableA2Tests
{
    private static readonly Dictionary<string, string> Seeds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["user"] = "ViewerOne",
    };

    public static TheoryData<string, string> Rows =>
        new()
        {
            // slices
            { "{text.first:3|hello}", "hel" },
            { "{text.last:3|hello}", "llo" },
            { "{text.substring:2|3|hello}", "ell" },
            { "{text.length.fit:hello|3}", "hel" },
            { "{text.initials:big world}", "BW" },
            { @"{text.lines.first:a\nb}", "a" },
            { @"{text.lines.join:a\nb|\s/\s}", "a / b" },
            // naming styles
            { "{text.slug:Hello Big World!}", "hello-big-world" },
            { "{text.slug:Crème Brûlée}", "creme-brulee" },
            { "{text.snake:Hello Big World}", "hello_big_world" },
            { "{text.kebab:Hello Big World}", "hello-big-world" },
            { "{text.camel:hello big world}", "helloBigWorld" },
            { "{text.pascal:hello big world}", "HelloBigWorld" },
            { "{text.hashtag:big world}", "#BigWorld" },
            { "{text.snake:helloBigWorld}", "hello_big_world" },
            // encodings
            { "{text.url.encode:a b&c}", "a%20b%26c" },
            { "{text.url.decode:a%20b}", "a b" },
            { "{text.query.encode:a b}", "a+b" },
            { "{text.path.encode:a/b c}", "a%2Fb%20c" },
            { "{text.base64.encode:hi}", "aGk=" },
            { "{text.base64.decode:aGk=}", "hi" },
            { "{text.base64.decode:***}", "{text.base64.decode:***}" },
            { "{text.html.strip:<b>hi</b>}", "hi" },
            // stripping and filtering
            { "{text.links.strip:see https://a.b now}", "see now" },
            { "{text.mentions.strip:@ViewerTwo hi}", "hi" },
            { "{text.digits:a1b22}", "122" },
            { "{text.letters:a1b2}", "ab" },
            { "{text.vowels.remove:hello}", "hll" },
            { "{text.censor:you noob|noob}", "you ****" },
            { "{text.censor:you NOOB|noob}", "you ****" },
            { "{text.mask:secret|2}", "****et" },
            { "{text.mask:ab|5}", "ab" },
            // fancy alphabets
            { "{text.smallcaps:hello}", "ʜᴇʟʟᴏ" },
            { "{text.upsidedown:hello}", "ollǝɥ" },
            { "{text.fullwidth:hi}", "ｈｉ" },
            { "{text.bold:hi}", "𝐡𝐢" },
            { "{text.italic:hi}", "𝘩𝘪" },
            { "{text.monospace:hi}", "𝚑𝚒" },
            { "{text.script:hi}", "𝒽𝒾" },
            { "{text.script:Be}", "ℬℯ" },
            { "{text.bubble:hi}", "ⓗⓘ" },
            { "{text.squared:HI}", "🄷🄸" },
            { "{text.strike:hi}", "h̶i̶" },
            { "{text.underline:hi}", "h̲i̲" },
            { "{text.superscript:hi}", "ʰⁱ" },
            { "{text.plain:𝐡𝐢 ⓗⓘ}", "hi hi" },
            // playful
            { "{text.leet:leet}", "l337" },
            { "{text.rot13:hello}", "uryyb" },
            { "{text.uwu:really}", "weawwy" },
            { "{text.clap:big world}", "big 👏 world" },
            { "{text.clap:big world|🔥}", "big 🔥 world" },
            { "{text.stutter:hello}", "h-hello" },
            { "{text.yell:hello}", "HELLO!" },
            { "{text.escape.chat:/ban x}", "⁄ban x" },
            { "{text.escape.chat:.me x}", "․me x" },
            { "{text.escape.chat:hi /x}", "hi /x" },
            // plural forms
            { "{text.plural:3|sub|subs}", "subs" },
            { "{text.plural:1|sub|subs}", "sub" },
            { "{text.plural.count:3|sub|subs}", "3 subs" },
            { "{text.plural.count:1|sub|subs}", "1 sub" },
            { "{text.plural.auto:2|box}", "boxes" },
            { "{text.plural.auto:1|box}", "box" },
            { "{text.singular:boxes}", "box" },
            { "{text.article:apple}", "an apple" },
            { "{text.article:banana}", "a banana" },
            { "{text.article:hour}", "an hour" },
            { "{text.article:unicorn}", "a unicorn" },
            { "{text.possessive:James}", "James'" },
            { "{text.possessive:Anna}", "Anna's" },
            { "{text.join.and:a|b|c}", "a, b and c" },
            { "{text.join.and:a|b}", "a and b" },
            { "{text.join.and:a}", "a" },
            { "{text.join.or:a|b|c}", "a, b or c" },
            // nesting and bad arguments
            { "{text.initials:{user}}", "V" },
            { "{text.first:x|hello}", "{text.first:x|hello}" },
            { "{text.substring:0|2|hello}", "{text.substring:0|2|hello}" },
            { "{text.plural:x|a|b}", "{text.plural:x|a|b}" },
            { "{text.mask:secret|x}", "{text.mask:secret|x}" },
            { "{text.censor:abc|}", "{text.censor:abc|}" },
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

    [Fact]
    public async Task The_catalog_example_of_lines_join_keeps_a_trimmed_separator_as_written()
    {
        string rendered = await RenderAsync(@"{text.lines.join:a\nb| / }");

        rendered.Should().Be("a/b");
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

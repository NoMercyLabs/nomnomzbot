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

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Logic;

/// <summary>
/// Catalog section 7.5, the pure logic rows: every Example renders to exactly its Output cell through the real
/// parser, registry and resolver, against the section 5 fixture (given as seed variables).
/// </summary>
public sealed class LogicHelpersTableTests
{
    internal static readonly Dictionary<string, string> Fixture = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["user.isSub"] = "true",
        ["user.isVip"] = "false",
        ["user.isMod"] = "false",
        ["stream.isLive"] = "true",
        ["stream.live"] = "true",
        ["stream.game"] = "Software and Game Development",
        ["stream.viewers"] = "42",
        ["user.login"] = "viewerone",
        ["user.display"] = "ViewerOne",
        ["user.pronouns"] = "she/her",
        ["user.months"] = "7",
        ["target.display"] = "ViewerTwo",
        ["count.deaths"] = "12",
        ["count.wins"] = "3",
        ["args.all"] = "@ViewerTwo hello big world",
        ["args.1"] = "@ViewerTwo",
        ["args.2"] = "hello",
        ["args.5"] = "",
        ["user"] = "ViewerOne",
    };

    public static TheoryData<string, string> Rows =>
        new()
        {
            { "{if:{user.isSub}|thanks sub!|sub for perks}", "thanks sub!" },
            { "{if:{user.isVip}|💎}", "" },
            { "{if:{user.isVip}|💎|plain}", "plain" },
            { "{if.stream.live:on|off}", "on" },
            { "{if.user.login.equals:viewerone:hi me|hi}", "hi me" },
            { "{if.user.login.equals:ViewerOne:hi me|hi}", "hi" },
            { "{if.user.login.iequals:VIEWERONE:yes|no}", "yes" },
            { "{if.args.all.contains:hello:hi back|}", "hi back" },
            { "{if.args.1.startswith:@:mention|word}", "mention" },
            { "{if.args.all.endswith:world:yes|no}", "yes" },
            { "{if.args.2.matches:^h:h-word|other}", "h-word" },
            { "{if.count.deaths.gt:10:rough day|ok}", "rough day" },
            { "{if.count.deaths.gt:12:rough day|ok}", "ok" },
            { "{if.stream.viewers.lt:10:cozy|busy}", "busy" },
            { "{if.user.months.gte:6:veteran|new}", "veteran" },
            { "{if.count.wins.lte:3:keep going|great}", "keep going" },
            { "{if.count.wins.lte:2:keep going|great}", "great" },
            { "{if.not:{user.isMod}|not a mod|mod}", "not a mod" },
            { "{if.all:{user.isSub}|{stream.isLive}|both|no}", "both" },
            { "{if.all:{user.isSub}|{user.isVip}|both|no}", "no" },
            { "{if.any:{user.isMod}|{user.isSub}|yes|no}", "yes" },
            { "{if.any:{user.isMod}|{user.isVip}|yes|no}", "no" },
            { "{if.empty:{args.1}|who?|hi {args.1}}", "hi @ViewerTwo" },
            { "{if.empty:{args.5}|who?|hi {args.5}}", "who?" },
            { "{default:{stream.game}|nothing}", "Software and Game Development" },
            { "{default:{args.5}|nobody}", "nobody" },
            { "{coalesce:{args.5}|{target.display}|{user.display}}", "ViewerTwo" },
            { "{coalesce:{args.5}|}", "" },
            { "{switch:{user.pronouns}|she/her=queen|he/him=king|royal}", "queen" },
            { "{switch:they/them|she/her=queen|he/him=king|royal}", "royal" },
            { "{switch:he/him|she/her=queen|he/him=king}", "king" },
            { "{switch.range:{count.deaths}|5=fine|10=hmm|oof}", "oof" },
            { "{switch.range:7|5=fine|10=hmm|oof}", "hmm" },
            { "{switch.range:5|5=fine|10=hmm|oof}", "fine" },
            { "{chance:100|jackpot!|no luck}", "jackpot!" },
            { "{chance:0|jackpot!|no luck}", "no luck" },
            { "{chance:0|jackpot!}", "" },
            { "{literal:{user}}", "{user}" },
            { "{literal:a|b}", "a|b" },
            { "a{chars.nothing}b", "ab" },
            { "a{chars.newline}b", "a\nb" },
            { "a{chars.space}b", "a b" },
            { "a{chars.pipe}b", "a|b" },
            { "@{chars.zws}everyone", "@" + char.ConvertFromUtf32(0x200B) + "everyone" },
            // The short forms are retired: a data migration rewrote saved text, so they print as written.
            { "a{nothing}b", "a{nothing}b" },
            { "a{newline}b", "a{newline}b" },
            { "a{space}b", "a{space}b" },
            { "a{pipe}b", "a{pipe}b" },
            { "@{zws}everyone", "@{zws}everyone" },
            { "{pick:solo}", "solo" },
            { "{pick.inline:solo}", "solo" },
            { "{pick.weighted:0=rare|5=common}", "common" },
            { "{random.number:1}", "1" },
            { "{random.number:7|7}", "7" },
            { "{random.decimal:2|2|2}", "2.00" },
            // Bad arguments print the token as written.
            { "{if.count.deaths.gt:abc:x|y}", "{if.count.deaths.gt:abc:x|y}" },
            { "{if.args.2.matches:(:x|y}", "{if.args.2.matches:(:x|y}" },
            { "{switch.range:abc|5=fine|oof}", "{switch.range:abc|5=fine|oof}" },
            { "{chance:abc|a|b}", "{chance:abc|a|b}" },
            { "{random.number:abc}", "{random.number:abc}" },
            { "{random.number:9|3}", "{random.number:9|3}" },
            { "{pick.weighted:x=a|y=b}", "{pick.weighted:x=a|y=b}" },
            // The as-built random keys keep resolving through the as-built code.
            { "{random.number.1}", "1" },
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
    public async Task A_pick_draws_one_of_its_items()
    {
        string rendered = await RenderAsync("{pick:rock|paper|scissors}");

        new[] { "rock", "paper", "scissors" }.Should().Contain(rendered);
    }

    [Fact]
    public async Task A_pick_never_repeats_the_item_it_drew_last_for_the_same_template()
    {
        string first = await RenderAsync("{pick:left-no-repeat|right-no-repeat}");
        string second = await RenderAsync("{pick:left-no-repeat|right-no-repeat}");

        second.Should().NotBe(first);
    }

    [Fact]
    public async Task An_inline_pick_draws_one_of_its_items_and_keeps_an_escaped_pipe_inside_one()
    {
        string rendered = await RenderAsync(@"{pick.inline:a\|b;a\|b}");

        rendered.Should().Be("a|b");
    }

    [Fact]
    public async Task A_boolean_path_that_nothing_resolves_takes_the_else_branch()
    {
        string rendered = await RenderAsync("{if.nothing.known.here:yes|no}");

        rendered.Should().Be("no");
    }

    [Fact]
    public async Task A_value_that_a_helper_provides_drives_the_condition()
    {
        string rendered = await RenderAsync("{if.chars.pipe:pipe is set|no}");

        rendered.Should().Be("pipe is set");
    }

    private static async Task<string> RenderAsync(string template)
    {
        await using ServiceProvider services = new ServiceCollection()
            .AddTemplateHelpers(typeof(TemplateResolver).Assembly)
            .BuildServiceProvider();
        return await TextHelpersTableTests
            .BuildResolver(services.GetRequiredService<ITemplateHelperRegistry>())
            .ResolveAsync(template, Fixture, broadcasterId: null);
    }
}

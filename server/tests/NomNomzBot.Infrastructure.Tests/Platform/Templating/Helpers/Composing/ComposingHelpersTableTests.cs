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

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Composing;

/// <summary>
/// Catalog section 7.5, the quote, set/var, include and chatter rows: every Example renders to exactly its
/// Output cell through the real parser, registry and resolver, against the section 5 fixture (viewer ViewerOne).
/// </summary>
public sealed class ComposingHelpersTableTests
{
    public static TheoryData<string, string> Rows =>
        new()
        {
            // quote
            { "{quote:12}", "#12 \"that was close\"" },
            { "{quote.text:12}", "that was close" },
            { "{quote.author:12}", "ViewerTwo" },
            { "{quote.game:12}", "Just Chatting" },
            { "{quote.date:12}", "2026-03-01" },
            { "{quote.count}", "48" },
            { "{quote.latest}", "#48 \"chat is wild\"" },
            { "{random.quote}", "#12 \"that was close\" (ViewerTwo, 2026-03-01)" },
            // A quote without author, game or date prints only what it has.
            { "{quote:5}", "#5 \"bare\"" },
            { "{quote.author:5}", "" },
            { "{quote.game:5}", "" },
            { "{quote.date:5}", "" },
            // The date is the channel-local day: 23:30Z is already the next day in Amsterdam.
            { "{quote.date:7}", "2026-03-02" },
            { "{quote:7}", "#7 \"late night\"" },
            // A missing or bad number prints the token as written.
            { "{quote:99}", "{quote:99}" },
            { "{quote.text:abc}", "{quote.text:abc}" },
            { "{quote.text:0}", "{quote.text:0}" },
            { "{quote}", "{quote}" },
            { "{quote.count:5}", "{quote.count:5}" },
            // set and var
            { "{set:x={user}}hi {x}", "hi ViewerOne" },
            { "{set:x=5}{var.x}", "5" },
            { "{set:greet=hey}{greet}", "hey" },
            { "{set:greet=hey}{GREET} {var.Greet}", "hey hey" },
            { "{set:x=1}{set:x=2}{var.x}", "2" },
            { "{set:x=a|b}{var.x}", "a|b" },
            { "{set:u=a:b}{var.u}", "a:b" },
            { "{set:x=}[{var.x}]", "[]" },
            { "{set:x=5}", "" },
            { "{var.nope}", "{var.nope}" },
            { "{nope}", "{nope}" },
            { "{set:x}", "{set:x}" },
            { "{set:bad name=1}", "{set:bad name=1}" },
            { "{set:a.b=1}", "{set:a.b=1}" },
            // A seed variable keeps its name: set refuses it.
            { "{set:user=zz}{user}", "{set:user=zz}ViewerOne" },
            // include.command and command.<name>
            { "{include.command:hug}", "ViewerOne sends a hug" },
            { "{command.hug}", "ViewerOne sends a hug" },
            { "{include.command:HUG}", "ViewerOne sends a hug" },
            { "say: {command.hug}!", "say: ViewerOne sends a hug!" },
            { "{include.command:pipelined}", "{include.command:pipelined}" },
            { "{include.command:nope}", "{include.command:nope}" },
            { "{command.nope}", "{command.nope}" },
            { "{include.command:}", "{include.command:}" },
            // A command that includes itself, or two that include each other, stops at the repeat.
            { "{include.command:selfy}", "{include.command:selfy}" },
            { "{include.command:loopa}", "a>b>{include.command:loopa}" },
            // A long chain of different commands stops at the depth limit (5 nested includes).
            { "{include.command:c1}", "1>2>3>4>5>{include.command:c6}" },
            { "{include.command:c7}", "end" },
            // include.list
            { "{include.list:snacks|2}", "cookies" },
            { "{include.list:snacks|1}", "chips" },
            { "{include.list:snacks|9}", "" },
            { "{include.list:nolist|1}", "" },
            { "{include.list:snacks|x}", "{include.list:snacks|x}" },
            { "{include.list:snacks|0}", "{include.list:snacks|0}" },
            { "{include.list:snacks}", "{include.list:snacks}" },
            // random.chatter.notMe: the only other chatter is ViewerTwo.
            { "{random.chatter.notMe}", "ViewerTwo" },
            { "{random.chatter.notMe} and {random.chatter.notMe}", "ViewerTwo and ViewerTwo" },
            { "{random.chatter:1}", "{random.chatter:1}" },
        };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task The_example_renders_to_exactly_the_output_cell(
        string template,
        string expected
    )
    {
        await using ComposingHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(template, ComposingHelperFixture.ChannelA);

        rendered.Should().Be(expected);
    }

    [Fact]
    public async Task Without_a_channel_the_channel_bound_rows_print_as_written()
    {
        await using ComposingHelperFixture fixture = new();

        (await fixture.RenderAsync("{quote:12}", null)).Should().Be("{quote:12}");
        (await fixture.RenderAsync("{quote.count}", null)).Should().Be("{quote.count}");
        (await fixture.RenderAsync("{include.command:hug}", null))
            .Should()
            .Be("{include.command:hug}");
        (await fixture.RenderAsync("{include.list:snacks|1}", null))
            .Should()
            .Be("{include.list:snacks|1}");
        (await fixture.RenderAsync("{random.chatter}", null)).Should().Be("{random.chatter}");
    }
}

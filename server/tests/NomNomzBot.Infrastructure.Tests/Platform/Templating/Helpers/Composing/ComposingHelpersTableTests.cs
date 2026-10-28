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
            // include.cmd. {command.*} is the running-command info, so {command.hug} is never an include.
            { "{include.cmd:hug}", "ViewerOne sends a hug" },
            { "{include.cmd:HUG}", "ViewerOne sends a hug" },
            { "say: {include.cmd:hug}!", "say: ViewerOne sends a hug!" },
            { "{command.hug}", "{command.hug}" },
            { "{include.command:hug}", "{include.command:hug}" },
            { "{include.cmd:pipelined}", "{include.cmd:pipelined}" },
            { "{include.cmd:nope}", "{include.cmd:nope}" },
            { "{include.cmd:}", "{include.cmd:}" },
            // A command that includes itself, or two that include each other, stops at the repeat.
            { "{include.cmd:selfy}", "{include.cmd:selfy}" },
            { "{include.cmd:loopa}", "a>b>{include.cmd:loopa}" },
            // A long chain of different commands stops at the depth limit (5 nested includes).
            { "{include.cmd:c1}", "1>2>3>4>5>{include.cmd:c6}" },
            { "{include.cmd:c7}", "end" },
            // include.builtin: only a read-only built-in that any viewer may use runs.
            { "{include.builtin:uptime}", "The stream has been live for 2h 5m." },
            { "{include.builtin:UPTIME}", "The stream has been live for 2h 5m." },
            { "up: {include.builtin:uptime}!", "up: The stream has been live for 2h 5m.!" },
            { "{include.builtin:banhammer}", "{include.builtin:banhammer}" },
            { "{include.builtin:modonly}", "{include.builtin:modonly}" },
            { "{include.builtin:failing}", "{include.builtin:failing}" },
            { "{include.builtin:nope}", "{include.builtin:nope}" },
            { "{include.builtin:}", "{include.builtin:}" },
            { "{include.builtin:uptime|x}", "{include.builtin:uptime|x}" },
            // A built-in whose reply includes itself stops at the depth limit (5 nested includes).
            { "{include.builtin:loopy}", "loopy:loopy:loopy:loopy:loopy:{include.builtin:loopy}" },
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
    public async Task An_included_built_in_runs_as_the_viewer_who_triggered_the_template()
    {
        await using ComposingHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{include.builtin:whoami}",
            ComposingHelperFixture.ChannelA,
            new Dictionary<string, string>
            {
                ["user"] = "ViewerOne",
                ["user.id"] = "1001",
                ["user.name"] = "viewerone",
                ["user.provider"] = "twitch",
            }
        );

        rendered
            .Should()
            .Be("ViewerOne (viewerone) on twitch, prefix !, tone informative, level 0, args []");
    }

    [Fact]
    public async Task Without_a_channel_the_channel_bound_rows_print_as_written()
    {
        await using ComposingHelperFixture fixture = new();

        (await fixture.RenderAsync("{quote:12}", null)).Should().Be("{quote:12}");
        (await fixture.RenderAsync("{quote.count}", null)).Should().Be("{quote.count}");
        (await fixture.RenderAsync("{include.cmd:hug}", null)).Should().Be("{include.cmd:hug}");
        (await fixture.RenderAsync("{include.builtin:uptime}", null))
            .Should()
            .Be("{include.builtin:uptime}");
        (await fixture.RenderAsync("{include.list:snacks|1}", null))
            .Should()
            .Be("{include.list:snacks|1}");
        (await fixture.RenderAsync("{random.chatter}", null)).Should().Be("{random.chatter}");
    }
}

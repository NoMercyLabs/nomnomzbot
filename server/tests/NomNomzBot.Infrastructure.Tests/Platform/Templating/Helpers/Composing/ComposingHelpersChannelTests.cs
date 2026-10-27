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

/// <summary>The chatter draw (seeded) and the per-channel isolation of the composing rows.</summary>
public sealed class ComposingHelpersChannelTests
{
    private static readonly string[] ChattersA = ["ViewerOne", "ViewerTwo"];

    [Fact]
    public async Task The_chatter_draw_uses_the_injected_random_over_the_sorted_channel_chatters()
    {
        await using ComposingHelperFixture fixture = new();
        string expected = ChattersA[new Random(1).Next(ChattersA.Length)];

        string rendered = await fixture.RenderAsync(
            "{random.chatter}",
            ComposingHelperFixture.ChannelA
        );

        rendered.Should().Be(expected);
    }

    [Fact]
    public async Task The_mention_row_is_the_drawn_chatter_with_an_at_sign()
    {
        await using ComposingHelperFixture fixture = new();
        string expected = "@" + ChattersA[new Random(1).Next(ChattersA.Length)];

        string rendered = await fixture.RenderAsync(
            "{random.chatter.mention}",
            ComposingHelperFixture.ChannelA
        );

        rendered.Should().Be(expected);
    }

    [Fact]
    public async Task Many_draws_stay_inside_the_chatters_and_reach_both_of_them()
    {
        await using ComposingHelperFixture fixture = new();
        List<string> draws = [];

        for (int i = 0; i < 40; i++)
            draws.Add(
                await fixture.RenderAsync("{random.chatter}", ComposingHelperFixture.ChannelA)
            );

        draws.Distinct().Order().Should().Equal(ChattersA);
    }

    [Fact]
    public async Task The_not_me_row_leaves_out_the_triggering_viewer_by_id()
    {
        await using ComposingHelperFixture fixture = new();

        string asOne = await fixture.RenderAsync(
            "{random.chatter.notMe}",
            ComposingHelperFixture.ChannelA,
            new Dictionary<string, string> { ["user.id"] = "1001" }
        );
        string asTwo = await fixture.RenderAsync(
            "{random.chatter.notMe}",
            ComposingHelperFixture.ChannelA,
            new Dictionary<string, string> { ["user.id"] = "1002" }
        );

        asOne.Should().Be("ViewerTwo");
        asTwo.Should().Be("ViewerOne");
    }

    [Fact]
    public async Task The_not_me_row_is_empty_when_the_viewer_is_the_only_chatter()
    {
        await using ComposingHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "[{random.chatter.notMe}]",
            ComposingHelperFixture.ChannelB,
            new Dictionary<string, string> { ["user.id"] = "2001" }
        );

        rendered.Should().Be("[]");
    }

    [Fact]
    public async Task Two_channels_never_mix_their_chatters_quotes_commands_or_lists()
    {
        await using ComposingHelperFixture fixture = new();
        Guid b = ComposingHelperFixture.ChannelB;

        (await fixture.RenderAsync("{random.chatter}", b)).Should().Be("OtherViewer");
        (await fixture.RenderAsync("{quote.text:12}", b)).Should().Be("other channel");
        (await fixture.RenderAsync("{quote.count}", b)).Should().Be("1");
        (await fixture.RenderAsync("{command.hug}", b)).Should().Be("other hug");
        (await fixture.RenderAsync("{include.list:snacks|1}", b)).Should().Be("tea");
        (await fixture.RenderAsync("{command.loopa}", b)).Should().Be("{command.loopa}");
    }

    [Fact]
    public async Task A_set_value_lives_only_in_its_own_render()
    {
        await using ComposingHelperFixture fixture = new();

        await fixture.RenderAsync("{set:x=5}", ComposingHelperFixture.ChannelA);
        string second = await fixture.RenderAsync("{var.x}{x}", ComposingHelperFixture.ChannelA);

        second.Should().Be("{var.x}{x}");
    }
}

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
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Counters;

/// <summary>
/// Catalog section 7.8, the <c>list.*</c> rows: every Example renders to exactly its Output cell through the
/// real parser, registry and resolver, against the section 5 fixture (pick list <c>snacks</c>).
/// </summary>
public sealed class ListHelpersTableTests
{
    public static TheoryData<string, string> Rows =>
        new()
        {
            { "{list.length:snacks}", "3" },
            { "{list.item:snacks|2}", "cookies" },
            { "{list.first:snacks}", "chips" },
            { "{list.last:snacks}", "fruit" },
            { "{list.join:snacks}", "chips, cookies, fruit" },
            { @"{list.join:snacks|\s/\s}", "chips / cookies / fruit" },
            { "{list.join.and:snacks}", "chips, cookies and fruit" },
            { "{list.sorted:snacks}", "chips, cookies, fruit" },
            { "{list.contains:snacks|chips}", "true" },
            { "{list.contains:snacks|CHIPS}", "true" },
            { "{list.contains:snacks|beer}", "false" },
            { "{list.indexOf:snacks|fruit}", "3" },
            { "{list.indexOf:snacks|beer}", "0" },
            { "{list.inline.length:a;b;c}", "3" },
            { "{list.inline.item:2|a;b;c}", "b" },
            { @"{list.inline.join:\s+\s|a;b}", "a + b" },
            { "{list.count}", "7" },
            // Edge cases the catalog implies: ordering, short lists, missing lists, bad arguments.
            { "{list.sorted:mixed}", "Apple, fig, pear" },
            { "{list.join.and:pair}", "x and y" },
            { "{list.join.and:single}", "only" },
            { "{list.length:nolist}", "0" },
            { "{list.first:nolist}", "" },
            { "{list.item:snacks|9}", "" },
            { "{list.item:snacks|0}", "{list.item:snacks|0}" },
            { "{list.item:snacks|two}", "{list.item:snacks|two}" },
            { "{list.item:snacks}", "{list.item:snacks}" },
            { "{list.length:}", "{list.length:}" },
            { "{list.inline.item:4|a;b;c}", "" },
            { "{list.inline.length:}", "0" },
            { "{list.pick:nolist}", "" },
        };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task The_example_renders_to_exactly_the_output_cell(
        string template,
        string expected
    )
    {
        await using ListHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(template, ListHelperFixture.ChannelA);

        rendered.Should().Be(expected);
    }

    [Fact]
    public async Task The_pick_row_renders_one_entry_of_the_list()
    {
        await using ListHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{list.pick:snacks}",
            ListHelperFixture.ChannelA
        );

        new[] { "chips", "cookies", "fruit" }.Should().Contain(rendered);
    }

    [Fact]
    public async Task The_shuffle_row_renders_a_permutation_joined_with_a_comma()
    {
        await using ListHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{list.shuffle:snacks}",
            ListHelperFixture.ChannelA
        );

        rendered.Split(", ").Order().Should().Equal("chips", "cookies", "fruit");
    }

    [Fact]
    public async Task The_random_index_row_renders_a_position_inside_the_list()
    {
        await using ListHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{list.random.index:snacks}",
            ListHelperFixture.ChannelA
        );

        new[] { "1", "2", "3" }.Should().Contain(rendered);
    }

    [Fact]
    public async Task Two_channels_never_mix_their_lists()
    {
        await using ListHelperFixture fixture = new();

        string forA = await fixture.RenderAsync("{list.join:snacks}", ListHelperFixture.ChannelA);
        string forB = await fixture.RenderAsync("{list.join:snacks}", ListHelperFixture.ChannelB);
        string countB = await fixture.RenderAsync("{list.count}", ListHelperFixture.ChannelB);

        forA.Should().Be("chips, cookies, fruit");
        forB.Should().Be("tea, toast");
        countB.Should().Be("1");
    }

    [Fact]
    public async Task A_render_with_no_channel_leaves_a_named_list_token_as_written()
    {
        await using ListHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync("{list.length:snacks}", broadcasterId: null);
        string inline = await fixture.RenderAsync("{list.inline.length:a;b}", broadcasterId: null);

        rendered.Should().Be("{list.length:snacks}");
        inline.Should().Be("2");
    }

    [Fact]
    public async Task The_as_built_pick_form_still_resolves_through_the_legacy_pre_pass()
    {
        await using ListHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{list.pick.snacks}",
            ListHelperFixture.ChannelA
        );

        new[] { "chips", "cookies", "fruit" }.Should().Contain(rendered);
    }

    [Fact]
    public async Task The_provider_claims_only_its_own_keys_and_never_the_as_built_pick_prefix()
    {
        await using ListHelperFixture fixture = new();
        ITemplateHelperRegistry registry = fixture.Registry;

        registry.ProviderFor("list.length").Should().NotBeNull();
        registry.ProviderFor("list.pick.snacks").Should().BeNull();
        registry.ProviderFor("count.deaths").Should().BeNull();
    }
}

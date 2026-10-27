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
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Counters;
using NomNomzBot.Infrastructure.Tests.Localization;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Counters;

/// <summary>The list provider as the registry sees it: one namespace, its rows, its strings and seeded randomness.</summary>
public sealed class ListTemplateHelpersTests
{
    [Fact]
    public async Task The_scan_registers_one_list_provider_with_every_row_of_the_lane()
    {
        await using ListHelperFixture fixture = new();

        fixture.Registry.ProviderFor("list.length").Should().BeOfType<ListTemplateHelpers>();
        fixture
            .Registry.ProviderFor("list.inline.join")
            .Should()
            .BeSameAs(fixture.Registry.ProviderFor("list.length"));
        fixture
            .Registry.All.Where(e => e.Key.StartsWith("list.", StringComparison.Ordinal))
            .Where(e => e.Prefix is null && e.Key != "list.pick.<name>")
            .Should()
            .HaveCount(16);
    }

    [Fact]
    public void Every_entry_has_an_english_string_a_dutch_string_and_a_sample()
    {
        ListTemplateHelpers provider = new(
            new ServiceCollection()
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>()
        );
        DashboardStringsXmlCatalog strings = new();

        foreach (TemplateHelperEntry entry in provider.Entries)
        {
            strings
                .TryGetEnglish(entry.Description.Key, out string english)
                .Should()
                .BeTrue(entry.Key);
            strings.TryGetDutch(entry.Description.Key, out string dutch).Should().BeTrue(entry.Key);
            english.Should().NotBeNullOrWhiteSpace(entry.Key);
            dutch.Should().NotBe(english, entry.Key);
            entry.Sample.Should().NotBeNull(entry.Key);
        }
    }

    [Fact]
    public async Task Every_sample_is_plain_text_that_renders_to_itself_through_the_resolver()
    {
        await using ListHelperFixture fixture = new();

        foreach (
            TemplateHelperEntry entry in fixture.Registry.All.Where(e =>
                e.Key.StartsWith("list.", StringComparison.Ordinal) && e.Prefix is null
            )
        )
        {
            string rendered = await fixture.RenderAsync(entry.Sample, ListHelperFixture.ChannelA);

            rendered.Should().Be(entry.Sample, entry.Key);
        }
    }

    [Fact]
    public async Task A_seeded_random_gives_the_same_shuffle_every_time()
    {
        await using ListHelperFixture fixture = new();
        HelperNode node = new("list.shuffle", ["snacks"], "{list.shuffle:snacks}");

        string? first = await new ListRandomHelpers(new Random(1)).TryRenderAsync(
            node,
            fixture.SourceFor(ListHelperFixture.ChannelA),
            CancellationToken.None
        );
        string? second = await new ListRandomHelpers(new Random(1)).TryRenderAsync(
            node,
            fixture.SourceFor(ListHelperFixture.ChannelA),
            CancellationToken.None
        );

        first.Should().NotBeNull();
        first!.Split(", ").Order().Should().Equal("chips", "cookies", "fruit");
        second.Should().Be(first);
    }
}

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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Infrastructure.Tests.Localization;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Stream;

/// <summary>The stream and channel providers as the registry sees them: rows, strings, samples and fetches.</summary>
public sealed class StreamTemplateHelpersTests
{
    [Fact]
    public async Task The_scan_registers_a_stream_and_a_channel_provider_that_keep_the_as_built_keys_apart()
    {
        await using StreamHelperFixture fixture = new();

        fixture.Registry.ProviderFor("stream.uptime.long").Should().NotBeNull();
        fixture.Registry.ProviderFor("channel.followers").Should().NotBeNull();
        fixture
            .Registry.ProviderFor("stream.uptime.long")
            .Should()
            .NotBeSameAs(fixture.Registry.ProviderFor("channel.followers"));
        fixture.Registry.ProviderFor("stream.title").Should().BeNull();
        fixture.Registry.ProviderFor("stream.viewers").Should().BeNull();
        fixture.Registry.ProviderFor("channel.display").Should().BeNull();
        fixture.Registry.ProviderFor("stream.isLive").Should().BeNull();
    }

    [Fact]
    public async Task Every_entry_has_an_english_string_a_dutch_string_and_a_sample()
    {
        await using StreamHelperFixture fixture = new();
        DashboardStringsXmlCatalog strings = new();
        List<TemplateHelperEntry> entries =
        [
            .. fixture.Registry.All.Where(e =>
                fixture.Registry.ProviderFor(e.Key) is { Namespace: "stream" or "channel" }
            ),
        ];

        entries.Should().HaveCountGreaterThan(70);
        foreach (TemplateHelperEntry entry in entries)
        {
            strings
                .TryGetEnglish(entry.Description.Key, out string english)
                .Should()
                .BeTrue(entry.Key);
            strings.TryGetDutch(entry.Description.Key, out string dutch).Should().BeTrue(entry.Key);
            english.Should().NotBeNullOrWhiteSpace(entry.Key);
            dutch.Should().NotBe(english, entry.Key);
            entry.Sample.Should().NotBeNullOrEmpty(entry.Key);
        }
    }

    [Fact]
    public async Task Every_sample_is_plain_text_that_renders_to_itself_through_the_resolver()
    {
        await using StreamHelperFixture fixture = new();

        foreach (
            TemplateHelperEntry entry in fixture.Registry.All.Where(e =>
                fixture.Registry.ProviderFor(e.Key) is { Namespace: "stream" or "channel" }
            )
        )
        {
            string rendered = await fixture.RenderAsync(entry.Sample, StreamHelperFixture.ChannelA);

            rendered.Should().Be(entry.Sample, entry.Key);
        }
    }

    [Fact]
    public async Task One_render_asks_the_stats_service_once_for_the_union_of_the_metrics_it_needs()
    {
        await using StreamHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{stream.subs.new} {stream.subs.total} {stream.bits.total} {stream.viewers.peak}",
            StreamHelperFixture.ChannelA
        );

        rendered.Should().Be("3 10 1,250 57");
        fixture.RequestedMetrics.Should().HaveCount(1);
        fixture.RequestedMetrics[0].Should().BeEquivalentTo("subs", "bits", "peakViewers");
    }

    [Fact]
    public async Task A_render_that_needs_no_stream_metric_never_calls_the_stats_service()
    {
        await using StreamHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{stream.uptime.clock} {channel.followers}",
            StreamHelperFixture.ChannelA
        );

        rendered.Should().Be("2:15:00 1,234");
        fixture.RequestedMetrics.Should().BeEmpty();
    }

    [Fact]
    public async Task A_nested_token_that_needs_a_new_metric_still_renders()
    {
        await using StreamHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{stream.subs.new} {text.upper:{stream.topChatter}}",
            StreamHelperFixture.ChannelA
        );

        rendered.Should().Be("3 VIEWERONE (140)");
    }
}

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

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.LiveEvents;

public sealed class PollPredictionTemplateHelpersTests
{
    private static bool IsOurs(ITemplateHelperRegistry registry, TemplateHelperEntry entry) =>
        registry.ProviderFor(entry.Key) is { Namespace: "poll" or "prediction" };

    [Fact]
    public async Task The_scan_registers_a_poll_and_a_prediction_provider_and_leaves_the_event_seeds_alone()
    {
        await using PollPredictionHelperFixture fixture = new();

        fixture.Registry.ProviderFor("poll.question")!.Namespace.Should().Be("poll");
        fixture.Registry.ProviderFor("prediction.question")!.Namespace.Should().Be("prediction");
        fixture.Registry.ProviderFor("event.poll.title").Should().BeNull();
        fixture.Registry.ProviderFor("poll.winner").Should().BeNull();
    }

    [Fact]
    public async Task Every_entry_has_an_english_string_a_dutch_string_and_a_sample()
    {
        await using PollPredictionHelperFixture fixture = new();
        DashboardStringsXmlCatalog strings = new();
        List<TemplateHelperEntry> entries =
        [
            .. fixture.Registry.All.Where(e => IsOurs(fixture.Registry, e)),
        ];

        entries.Should().HaveCount(33);
        foreach (TemplateHelperEntry entry in entries)
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
        await using PollPredictionHelperFixture fixture = new();

        foreach (
            TemplateHelperEntry entry in fixture.Registry.All.Where(e =>
                IsOurs(fixture.Registry, e)
            )
        )
        {
            string rendered = await fixture.RenderAsync(
                entry.Sample,
                PollPredictionHelperFixture.ChannelA
            );

            rendered.Should().Be(entry.Sample, entry.Key);
        }
    }
}

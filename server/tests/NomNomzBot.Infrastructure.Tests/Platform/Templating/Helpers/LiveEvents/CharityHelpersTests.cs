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
using NomNomzBot.Infrastructure.Tests.Localization;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.LiveEvents;

public sealed class CharityHelpersTests
{
    public static TheoryData<string, string> Rows =>
        new()
        {
            { "{charity.name}", "Sample Charity" },
            { "{charity.raised}", "€420.00" },
            { "{charity.target}", "€1,000.00" },
            { "{charity.percent}", "42%" },
            { "{charity.bar}", "████░░░░░░ 42%" },
            { "{charity.link}", "https://www.samplecharity.org" },
            { "{charity.donations.count}", "17" },
            { "{charity.donations.latest}", "ViewerOne (€10.00)" },
        };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Every_row_renders_the_exact_output_of_the_catalog_fixture(
        string template,
        string expected
    )
    {
        await using LiveEventsHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(template);

        rendered.Should().Be(expected);
    }

    [Fact]
    public async Task With_no_campaign_every_row_prints_as_written()
    {
        await using LiveEventsHelperFixture fixture = new(idle: true);

        (await fixture.RenderAsync("{charity.name} {charity.raised} {charity.donations.latest}"))
            .Should()
            .Be("{charity.name} {charity.raised} {charity.donations.latest}");
    }

    [Fact]
    public async Task Campaign_tokens_fetch_the_campaign_once_and_the_donation_tokens_fetch_the_page_once()
    {
        await using LiveEventsHelperFixture fixture = new();

        await fixture.RenderAsync(
            "{charity.name} {charity.raised} {charity.target} {charity.percent} {charity.bar} "
                + "{charity.donations.count} {charity.donations.latest}"
        );

        await fixture
            .Charity.Received(1)
            .GetCharityCampaignAsync(
                LiveEventsHelperFixture.ChannelA,
                Arg.Any<CancellationToken>()
            );
        await fixture
            .Charity.ReceivedWithAnyArgs(1)
            .GetCharityCampaignDonationsAsync(default, default!, default);
    }

    [Fact]
    public async Task Every_entry_has_an_english_string_a_dutch_string_and_a_sample_that_renders_to_itself()
    {
        await using LiveEventsHelperFixture fixture = new();
        DashboardStringsXmlCatalog strings = new();
        List<TemplateHelperEntry> entries =
        [
            .. fixture.Registry.All.Where(e =>
                fixture.Registry.ProviderFor(e.Key) is { Namespace: "charity" }
            ),
        ];

        entries.Should().HaveCount(8);
        foreach (TemplateHelperEntry entry in entries)
        {
            strings
                .TryGetEnglish(entry.Description.Key, out string english)
                .Should()
                .BeTrue(entry.Key);
            strings.TryGetDutch(entry.Description.Key, out string dutch).Should().BeTrue(entry.Key);
            dutch.Should().NotBe(english, entry.Key);
            entry.Sample.Should().NotBeNullOrEmpty(entry.Key);
            (await fixture.RenderAsync(entry.Sample)).Should().Be(entry.Sample, entry.Key);
        }
    }
}

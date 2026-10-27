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
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.LiveEvents;

public sealed class HypeTrainHelpersTests
{
    public static TheoryData<string, string> Rows =>
        new()
        {
            { "{if:{hypetrain.isActive}|🚂 choo choo|}", "🚂 choo choo" },
            { "{hypetrain.level}", "3" },
            { "{hypetrain.progress}", "1,800" },
            { "{hypetrain.goal}", "3,000" },
            { "{hypetrain.percent}", "60%" },
            { "{hypetrain.bar}", "██████░░░░ 60%" },
            { "{hypetrain.remaining}", "1,200" },
            { "{hypetrain.timeLeft}", "4m 10s" },
            { "{hypetrain.total}", "5,400" },
            { "{hypetrain.topContributor}", "ViewerOne" },
            { "{hypetrain.record.level}", "5" },
            { "{if:{hypetrain.isGolden}|golden kappa!|}", "" },
            { "{if:{hypetrain.isShared}|shared train|}", "" },
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
    public async Task A_golden_kappa_shared_train_turns_both_flags_on()
    {
        await using LiveEventsHelperFixture fixture = new(
            trainType: "golden_kappa",
            sharedTrain: true
        );

        (await fixture.RenderAsync("{if:{hypetrain.isGolden}|golden kappa!|}"))
            .Should()
            .Be("golden kappa!");
        (await fixture.RenderAsync("{if:{hypetrain.isShared}|shared train|}"))
            .Should()
            .Be("shared train");
    }

    [Fact]
    public async Task With_no_train_the_flag_is_off_and_the_values_print_as_written()
    {
        await using LiveEventsHelperFixture fixture = new(idle: true);

        (await fixture.RenderAsync("{if:{hypetrain.isActive}|🚂 choo choo|no train}"))
            .Should()
            .Be("no train");
        (
            await fixture.RenderAsync(
                "{hypetrain.level} {hypetrain.timeLeft} {hypetrain.record.level}"
            )
        )
            .Should()
            .Be("{hypetrain.level} {hypetrain.timeLeft} {hypetrain.record.level}");
    }

    [Fact]
    public async Task Eight_tokens_in_one_render_fetch_the_status_once()
    {
        await using LiveEventsHelperFixture fixture = new();

        await fixture.RenderAsync(
            "{hypetrain.level} {hypetrain.progress} {hypetrain.goal} {hypetrain.percent} {hypetrain.bar} "
                + "{hypetrain.remaining} {hypetrain.timeLeft} {hypetrain.total}"
        );

        await fixture
            .HypeTrain.Received(1)
            .GetHypeTrainStatusAsync(
                LiveEventsHelperFixture.ChannelA,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Every_entry_has_an_english_string_a_dutch_string_and_a_sample_that_renders_to_itself()
    {
        await using LiveEventsHelperFixture fixture = new();
        DashboardStringsXmlCatalog strings = new();
        List<TemplateHelperEntry> entries =
        [
            .. fixture.Registry.All.Where(e =>
                fixture.Registry.ProviderFor(e.Key) is { Namespace: "hypetrain" }
            ),
        ];

        entries.Should().HaveCount(13);
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

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

public sealed class GoalHelpersTests
{
    public static TheoryData<string, string> Rows =>
        new()
        {
            { "{goal.followers.current}", "1,234" },
            { "{goal.followers.target}", "1,500" },
            { "{goal.followers.percent}", "82%" },
            { "{goal.followers.remaining}", "266" },
            { "{goal.followers.bar}", "████████░░ 82%" },
            { "{goal.followers.description}", "Road to 1.5k" },
            { "{goal.subs.current}", "80" },
            { "{goal.subs.target}", "100" },
            { "{goal.subs.percent}", "80%" },
            { "{goal.subs.remaining}", "20" },
            { "{goal.subs.bar}", "████████░░ 80%" },
            { "{goal.list}", "followers 82%, subs 80%" },
        };

    public static TheoryData<string, string> NewSubRows =>
        new()
        {
            { "{goal.newSubs.current}", "6" },
            { "{goal.newSubs.percent}", "30%" },
            { "{goal.list}", "followers 82%, subs 80%, new subs 30%" },
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

    [Theory]
    [MemberData(nameof(NewSubRows))]
    public async Task The_new_subscription_goal_rows_read_that_goal_type(
        string template,
        string expected
    )
    {
        await using LiveEventsHelperFixture fixture = new(withNewSubGoal: true);

        string rendered = await fixture.RenderAsync(template);

        rendered.Should().Be(expected);
    }

    [Fact]
    public async Task A_goal_type_the_channel_has_not_set_prints_the_token_as_written()
    {
        await using LiveEventsHelperFixture fixture = new();

        (await fixture.RenderAsync("{goal.newSubs.current}")).Should().Be("{goal.newSubs.current}");
    }

    [Fact]
    public async Task With_no_goals_every_row_prints_as_written()
    {
        await using LiveEventsHelperFixture fixture = new(idle: true);

        (await fixture.RenderAsync("{goal.followers.current} {goal.list}"))
            .Should()
            .Be("{goal.followers.current} {goal.list}");
    }

    [Fact]
    public async Task Six_tokens_in_one_render_fetch_the_goals_once()
    {
        await using LiveEventsHelperFixture fixture = new();

        await fixture.RenderAsync(
            "{goal.followers.current} {goal.followers.target} {goal.followers.bar} {goal.subs.percent} "
                + "{goal.subs.remaining} {goal.list}"
        );

        await fixture
            .Goals.Received(1)
            .GetCreatorGoalsAsync(LiveEventsHelperFixture.ChannelA, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Every_entry_has_an_english_string_a_dutch_string_and_a_sample_that_renders_to_itself()
    {
        await using LiveEventsHelperFixture fixture = new();
        DashboardStringsXmlCatalog strings = new();
        List<TemplateHelperEntry> entries =
        [
            .. fixture.Registry.All.Where(e =>
                fixture.Registry.ProviderFor(e.Key) is { Namespace: "goal" }
            ),
        ];

        entries.Should().HaveCount(14);
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

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
using NomNomzBot.Application.Contracts.Twitch;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Broadcast;

public sealed class AdScheduleHelpersBehaviorTests
{
    [Fact]
    public async Task The_scan_registers_an_ad_provider_and_a_schedule_provider()
    {
        await using BroadcastHelperFixture fixture = new();

        fixture.Registry.ProviderFor("ad.next.in")!.Namespace.Should().Be("ad");
        fixture.Registry.ProviderFor("schedule.list")!.Namespace.Should().Be("schedule");
        fixture.Registry.ProviderFor("ad.nope").Should().BeNull();
    }

    [Fact]
    public async Task Twelve_tokens_in_one_render_read_each_twitch_service_once()
    {
        await using BroadcastHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{ad.next.in} {ad.next.at} {ad.last.ago} {ad.snoozes.left} {ad.prerollFree} "
                + "{schedule.next.at} {schedule.next.title} {schedule.next.game} {schedule.week} {schedule.list:2}"
        );

        rendered
            .Should()
            .Be(
                "12m 15:42 48m 2 20m Friday 20:00 Bot night Software and Game Development Fri, Sun "
                    + "Fri 20:00 Bot night · Sun 14:00 Chill"
            );
        await fixture
            .Ads.Received(1)
            .GetAdScheduleAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await fixture
            .Schedule.Received(1)
            .GetScheduleAsync(
                Arg.Any<Guid>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_render_without_ad_or_schedule_tokens_calls_no_twitch_service()
    {
        await using BroadcastHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync("hello {text.upper:world} {schedule.link}");

        rendered.Should().Be("hello WORLD https://twitch.tv/samplechannel/schedule");
        await fixture
            .Ads.DidNotReceive()
            .GetAdScheduleAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await fixture
            .Schedule.DidNotReceive()
            .GetScheduleAsync(
                Arg.Any<Guid>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task With_no_ad_planned_the_state_reads_false_and_a_value_prints_the_token_as_written()
    {
        await using BroadcastHelperFixture fixture = new(noAd: true);

        string rendered = await fixture.RenderAsync(
            "{ad.isScheduled} {ad.next.in} {ad.last.ago} {if:{ad.isScheduled}|ad in {ad.next.in}|no ad planned}"
        );

        rendered.Should().Be("false {ad.next.in} {ad.last.ago} no ad planned");
    }

    [Fact]
    public async Task When_twitch_fails_every_token_prints_as_written()
    {
        await using BroadcastHelperFixture fixture = new(failing: true);

        string rendered = await fixture.RenderAsync(
            "{ad.next.in} {schedule.next.at} {schedule.week}"
        );

        rendered.Should().Be("{ad.next.in} {schedule.next.at} {schedule.week}");
    }

    [Fact]
    public async Task A_dutch_channel_gets_dutch_day_names_and_relative_words()
    {
        await using BroadcastHelperFixture fixture = new(language: "nl");

        string rendered = await fixture.RenderAsync(
            "{schedule.next.at} | {schedule.next.date} | {schedule.next.relative} | {schedule.next.full} | {ad.next.in.long}"
        );

        rendered
            .Should()
            .Be(
                "vrijdag 20:00 | vrijdag 30 oktober | over 4 dagen | volgende stream over 4 dagen, vrijdag 20:00 | 12 minuten"
            );
    }

    [Fact]
    public async Task A_zone_that_does_not_exist_leaves_the_viewer_time_token_as_written()
    {
        await using BroadcastHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{schedule.next.viewerTime:Not/AZone} {schedule.next.viewerTime} {schedule.list:x} {schedule.list:0}"
        );

        rendered
            .Should()
            .Be(
                "{schedule.next.viewerTime:Not/AZone} {schedule.next.viewerTime} {schedule.list:x} {schedule.list:0}"
            );
    }

    [Fact]
    public async Task A_planned_vacation_prints_its_window_and_its_end()
    {
        await using BroadcastHelperFixture fixture = new(vacation: true);

        string rendered = await fixture.RenderAsync(
            "{schedule.vacation} | {schedule.vacation.until}"
        );

        rendered.Should().Be("Fri 6 Nov - Sun 15 Nov | Sunday 15 November");
    }

    [Fact]
    public async Task A_segment_later_today_fills_today_and_the_week_and_becomes_the_next_stream()
    {
        await using BroadcastHelperFixture fixture = new(segmentToday: true);

        string rendered = await fixture.RenderAsync(
            "{schedule.today} | {schedule.week} | {schedule.next.at} | {schedule.next.title}"
        );

        rendered.Should().Be("21:00 Quick one | Mon, Fri, Sun | Monday 21:00 | Quick one");
    }

    [Fact]
    public async Task A_canceled_segment_is_never_listed_or_chosen()
    {
        await using BroadcastHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync("{schedule.list:10}");

        rendered.Should().NotContain("Canceled show").And.NotContain("Wed");
    }

    [Fact]
    public async Task Every_ad_and_schedule_entry_has_both_strings_a_sample_and_renders_to_itself()
    {
        await using BroadcastHelperFixture fixture = new();

        BroadcastEntryChecks.AssertAll(fixture.Registry, ["ad", "schedule"], minimumEntries: 28);
        foreach (
            TemplateHelperEntry entry in BroadcastEntryChecks.EntriesOf(
                fixture.Registry,
                ["ad", "schedule"]
            )
        )
        {
            string rendered = await fixture.RenderAsync(entry.Sample);

            rendered.Should().Be(entry.Sample, entry.Key);
        }
    }
}

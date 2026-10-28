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

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Broadcast;

public sealed class AdScheduleHelpersTableTests
{
    public static TheoryData<string, string> Rows =>
        new()
        {
            // Ads (catalog 7.11): next ad in 12m (90s long), last ad 48m ago, two snoozes left.
            { "{ad.next.in}", "12m" },
            { "{ad.next.in.long}", "12 minutes" },
            { "{ad.next.at}", "15:42" },
            { "{ad.next.seconds}", "720" },
            { "{ad.next.duration}", "1m 30s" },
            { "{ad.last.ago}", "48m" },
            { "{ad.last.at}", "14:42" },
            { "{ad.snoozes.left}", "2" },
            { "{ad.snoozes.refreshIn}", "34m" },
            { "{ad.prerollFree}", "20m" },
            { "{ad.isScheduled}", "true" },
            { "{if:{ad.isScheduled}|ad in {ad.next.in}|no ad planned}", "ad in 12m" },
            // Schedule: Friday 2026-10-30 20:00 local Bot night (3h, weekly), Sunday Chill, Tuesday Bot night.
            { "{schedule.next.at}", "Friday 20:00" },
            { "{schedule.next.date}", "Friday 30 October" },
            { "{schedule.next.in}", "4 days" },
            { "{schedule.next.relative}", "in 4 days" },
            { "{schedule.next.full}", "next stream in 4 days, Friday 20:00" },
            { "{schedule.next.title}", "Bot night" },
            { "{schedule.next.game}", "Software and Game Development" },
            { "{schedule.next.duration}", "3h" },
            { "{if:{schedule.next.isRecurring}|weekly|one-off}", "weekly" },
            { "{schedule.next.viewerTime:America/New_York}", "Fri 15:00" },
            { "{schedule.list:3}", "Fri 20:00 Bot night · Sun 14:00 Chill · Tue 20:00 Bot night" },
            { "{schedule.list:2}", "Fri 20:00 Bot night · Sun 14:00 Chill" },
            { "{schedule.week}", "Fri, Sun" },
            { "[{schedule.today}]", "[]" },
            { "[{schedule.vacation}]", "[]" },
            { "[{schedule.vacation.until}]", "[]" },
            { "{schedule.link}", "https://twitch.tv/samplechannel/schedule" },
            {
                "{schedule.ical}",
                "https://api.twitch.tv/helix/schedule/icalendar?broadcaster_id=123456"
            },
        };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Every_row_renders_the_exact_output_of_the_catalog_fixture(
        string template,
        string expected
    )
    {
        await using BroadcastHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(template);

        rendered.Should().Be(expected);
    }
}

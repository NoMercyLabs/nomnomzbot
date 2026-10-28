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

public sealed class ActivityHelpersTableTests
{
    public static TheoryData<string, string> Rows =>
        new()
        {
            // The ring: 18 messages in the last minute, 80 in five, 1,080 in the hour, 12 authors in ten minutes.
            { "{activity.mpm}", "18" },
            { "{activity.mpm:5}", "16" },
            { "{activity.messages.hour}", "1,080" },
            { "{activity.chatters.active:10}", "12" },
            { "{activity.top.hour}", "ViewerTwo" },
            { "{activity.last.chatter}", "ViewerTwo" },
            // The current stream.
            { "{activity.messages.stream}", "2,430" },
            { "{activity.top.stream}", "ViewerOne (140)" },
            { "{activity.top.list:3}", "ViewerOne 140, ViewerTwo 96, ViewerThree 40" },
            { "{activity.top.list:2}", "ViewerOne 140, ViewerTwo 96" },
            { "{activity.chatters.unique}", "33" },
            { "{activity.last.message.ago}", "4s" },
            { "{activity.commands.stream}", "88" },
            // Today, the chatter list and the busiest times.
            { "{activity.messages.today}", "2,610" },
            { "{activity.top.today}", "ViewerOne" },
            { "{activity.top.today.count}", "140" },
            { "{activity.chatters.now}", "51" },
            { "{activity.chatters.lurkers}", "18" },
            { "{activity.busiestHour}", "21:00" },
            { "{activity.busiestDay}", "Friday" },
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

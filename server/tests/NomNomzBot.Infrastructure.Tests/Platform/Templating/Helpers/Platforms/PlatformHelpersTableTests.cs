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

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Platforms;

public sealed class PlatformHelpersTableTests
{
    public static TheoryData<string, string> Rows =>
        new()
        {
            // The command was typed on Twitch, and the event came from Twitch.
            { "{platform.user}", "twitch" },
            { "{platform.user.name}", "Twitch" },
            { "{platform.user.icon}", "🟣" },
            { "{platform.event}", "twitch" },
            // Twitch 42, Kick 10, YouTube 8 viewers (60 in total).
            { "{platform.viewers.total}", "60" },
            { "{platform.viewers.twitch}", "42" },
            { "{platform.viewers.kick}", "10" },
            { "{platform.viewers.youtube}", "8" },
            { "{platform.viewers.breakdown}", "Twitch 42 · Kick 10 · YouTube 8" },
            { "{platform.viewers.share:kick}", "17%" },
            { "{platform.viewers.share:Twitch}", "70%" },
            { "{platform.live.list}", "Twitch, Kick, YouTube" },
            { "{platform.live.count}", "3" },
            { "{platform.isLive:youtube}", "true" },
            { "{if:{platform.isLive:youtube}|on YouTube too|}", "on YouTube too" },
            // Links of the three connections.
            { "{platform.link.twitch}", "https://twitch.tv/samplechannel" },
            { "{platform.link.kick}", "https://kick.com/samplechannel" },
            { "{platform.link.youtube}", "https://youtube.com/channel/UCsample" },
            { "{platform.link.youtube.live}", "https://youtube.com/watch?v=sample" },
            {
                "{platform.links}",
                "twitch.tv/samplechannel · kick.com/samplechannel · youtube.com/channel/UCsample"
            },
            // Totals.
            { "{platform.followers.twitch}", "1,234" },
            { "{platform.messages.stream:kick}", "120" },
            { "{platform.chatters.stream:youtube}", "6" },
            { "{platform.top}", "Twitch" },
            { "{platform.connected}", "Twitch, Kick, YouTube" },
            { "{platform.count}", "3" },
            { "{platform.title:youtube}", "Building a bot" },
            { "{platform.title:kick}", "Kick bot night" },
            { "{platform.title:twitch}", "Building a bot" },
        };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Every_row_renders_the_exact_output_of_the_catalog_fixture(
        string template,
        string expected
    )
    {
        await using PlatformHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(template);

        rendered.Should().Be(expected);
    }
}

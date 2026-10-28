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

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.People;

public sealed class PeopleHelpersTableTests
{
    public static TheoryData<string, string> Rows =>
        new()
        {
            // Names and links (catalog 7.7, the Example and Output columns).
            { "{user.display}", "ViewerOne" },
            { "{user.login}", "viewerone" },
            { "{user.mention}", "@ViewerOne" },
            { "{user.platform}", "twitch" },
            { "{user.platform.name}", "Twitch" },
            { "{target.mention}", "@ViewerTwo" },
            { "{target.shoutoutLink}", "https://twitch.tv/viewertwo" },
            // Profile.
            { "{if:{user.isFollower}|thanks for the follow|follow?}", "thanks for the follow" },
            { "{if:{user.isSub}|sub|}", "sub" },
            { "{if:{target.isSub}|sub|not a sub}", "not a sub" },
            { "{user.subTier}", "1" },
            { "{user.subTier.name}", "Tier 1" },
            { "{user.stats.watchTime.hours}", "12.5" },
            { "{user.stats.watchTime.long}", "12 hours, 30 minutes" },
            { "{user.stats.firstSeen.relative}", "one year ago" },
            { "{user.stats.lastSeen}", "2026-10-24" },
            { "{user.stats.lastSeen.relative}", "2 days ago" },
            { "{user.stats.bits}", "2,300" },
            // 1204 real messages against the default steps (10, 100, 500).
            { "{user.activity.level}", "veteran" },
            { "{target.activity.level}", "veteran" },
            { "{target.stats.lastSeen}", "2026-10-25" },
            { "{target.stats.bits}", "0" },
            // Follow record.
            { "{user.followedAt}", "2025-09-10" },
            { "{user.followAge.days}", "411" },
            { "{if:{target.isFollower}|follows|does not follow}", "follows" },
            // Streaks.
            { "{user.stats.streak}", "5" },
            { "{user.stats.streak.best}", "9" },
            { "{target.stats.streak}", "2" },
            // Points and ranking.
            { "{user.points}", "2,500" },
            { "{user.points.raw}", "2500" },
            { "{user.points} {user.points.name}", "2,500 nomz" },
            { "{user.points.rank}", "4" },
            { "{user.points.rank.ordinal}", "4th" },
            { "{user.points.nextRank.gap}", "300" },
            { "{user.points.nextRank.name}", "ViewerThree" },
            { "{target.points}", "800" },
            { "{target.points.rank}", "17" },
            // Twitch records.
            { "{user.color}", "#1E90FF" },
            { "{target.color}", "#FF4500" },
            { "{user.avatar}", "https://static-cdn.jtvnw.net/jtv_user_pictures/viewerone.png" },
            { "{target.avatar}", "https://static-cdn.jtvnw.net/jtv_user_pictures/viewertwo.png" },
            { "{user.bio}", "I like bots" },
            { "{target.bio}", "I build castles" },
            { "{user.createdAt}", "2022-08-14" },
            { "{target.createdAt}", "2019-05-02" },
            { "{user.accountAge.days}", "1534" },
            { "{target.accountAge}", "7 years, 5 months" },
            // The target's own channel (ViewerTwo is offline).
            { "{if:{target.isLive}|live now|offline}", "offline" },
            { "{target.viewers}", "0" },
            { "{target.game}", "Minecraft" },
            { "{target.title}", "Building castles" },
            // Stored data.
            { "{user.data.color}", "blue" },
            { "{user.data.color:none}", "blue" },
            { "{user.data.food:none}", "none" },
            { "{user.data.food}", "" },
            { "{user.quotes.count}", "3" },
            // Words around a row keep their place.
            { "hi {user.mention}, rank {user.points.rank.ordinal}!", "hi @ViewerOne, rank 4th!" },
        };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task The_example_renders_to_exactly_the_output_cell(
        string template,
        string expected
    )
    {
        await using PeopleHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(template, PeopleHelperFixture.ChannelA);

        rendered.Should().Be(expected);
    }

    [Fact]
    public async Task A_target_that_is_live_reports_the_stream_it_runs()
    {
        await using PeopleHelperFixture fixture = new();
        Dictionary<string, string> seeds = PeopleHelperFixture.Seeds("@StreamerFour");

        string live = await fixture.RenderAsync(
            "{if:{target.isLive}|live now|offline} {target.viewers}",
            PeopleHelperFixture.ChannelA,
            seeds
        );

        live.Should().Be("live now 18");
    }
}

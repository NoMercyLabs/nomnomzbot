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

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.People;

/// <summary>
/// <c>{user.activity.level}</c> and <c>{target.activity.level}</c>: the level of a viewer from the viewer's real
/// message count in the channel (ViewerOne has 1204 messages in channel A) against the channel's own steps.
/// </summary>
public sealed class PeopleActivityLevelTests
{
    [Theory]
    [InlineData(1, 100, 500, "veteran")] // 1204 is past every step
    [InlineData(1, 100, 1204, "veteran")] // a step is inclusive: exactly 1204 reaches veteran
    [InlineData(1, 100, 1205, "regular")] // one message short of veteran
    [InlineData(1, 1204, 5000, "regular")] // exactly 1204 reaches regular
    [InlineData(1, 1205, 5000, "casual")] // one message short of regular
    [InlineData(1204, 2000, 5000, "casual")] // exactly 1204 reaches casual
    [InlineData(1205, 2000, 5000, "new")] // one message short of casual
    public async Task A_channel_with_its_own_steps_changes_the_level_of_the_same_viewer(
        int casualFrom,
        int regularFrom,
        int veteranFrom,
        string expected
    )
    {
        await using PeopleHelperFixture fixture = new();
        fixture.UseChannelWithViewerSteps(
            PeopleHelperFixture.ChannelA,
            casualFrom,
            regularFrom,
            veteranFrom
        );

        string rendered = await fixture.RenderAsync(
            "{user.activity.level}",
            PeopleHelperFixture.ChannelA
        );

        rendered.Should().Be(expected);
    }

    [Fact]
    public async Task Each_channel_reads_its_own_steps()
    {
        await using PeopleHelperFixture fixture = new();

        // Channel B sets steps above the viewer's 1204 messages; channel A keeps the defaults.
        fixture.UseChannelWithViewerSteps(PeopleHelperFixture.ChannelB, 5000, 6000, 7000);

        string rendered = await fixture.RenderAsync(
            "A:{user.activity.level}",
            PeopleHelperFixture.ChannelA
        );
        string other = await fixture.RenderAsync(
            "B:{user.activity.level}",
            PeopleHelperFixture.ChannelB
        );

        rendered.Should().Be("A:veteran");
        other.Should().Be("B:new");
    }

    [Fact]
    public async Task A_viewer_with_no_profile_has_sent_no_messages_and_reads_as_new()
    {
        await using PeopleHelperFixture fixture = new();
        Dictionary<string, string> seeds = PeopleHelperFixture.Seeds("@StreamerFour");

        string rendered = await fixture.RenderAsync(
            "{target.activity.level}|{target.mention}",
            PeopleHelperFixture.ChannelA,
            seeds
        );

        rendered.Should().Be("new|@StreamerFour");
    }

    [Fact]
    public async Task The_mentioned_viewer_is_leveled_with_the_same_channel_steps()
    {
        await using PeopleHelperFixture fixture = new();
        fixture.UseChannelWithViewerSteps(PeopleHelperFixture.ChannelA, 1, 1205, 5000);

        string rendered = await fixture.RenderAsync(
            "{target.activity.level}",
            PeopleHelperFixture.ChannelA
        );

        rendered.Should().Be("casual");
    }
}

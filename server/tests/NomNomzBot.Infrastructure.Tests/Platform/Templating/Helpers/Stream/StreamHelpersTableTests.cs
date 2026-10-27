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

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Stream;

/// <summary>
/// Catalog section 7.6, the <c>stream.*</c> and <c>channel.*</c> rows: every Example renders to exactly its
/// Output cell through the real parser, registry and resolver, against the section 5 fixture.
/// </summary>
public sealed class StreamHelpersTableTests
{
    public static TheoryData<string, string> Rows =>
        new()
        {
            // Uptime and start time, from the channel's live state (live since 12:15Z, clock 14:30Z, zone +1).
            { "{stream.uptime.long}", "2 hours, 15 minutes" },
            { "{stream.uptime.clock}", "2:15:00" },
            { "{stream.uptime.minutes}", "135" },
            { "{stream.uptime.hours}", "2.25" },
            { "{stream.uptime.since}", "live since 13:15" },
            { "{stream.startedAt.local}", "13:15" },
            { "{stream.startedAt.relative}", "2 hours ago" },
            { "{stream.status}", "live" },
            { "{stream.id}", "40123456789" },
            // Title, category and viewers.
            { "{stream.title.short:8}", "Building…" },
            { "{stream.title.short:50}", "Building a bot" },
            { "{stream.game.short}", "Software and Game Development" },
            {
                "{stream.game.link}",
                "https://twitch.tv/directory/category/software-and-game-development"
            },
            {
                "{stream.game.boxart}",
                "https://static-cdn.jtvnw.net/ttv-boxart/509670-285x380.jpg"
            },
            { "{stream.viewers.compact}", "42" },
            { "{stream.viewers.peak}", "57" },
            // This stream's counts, from the stream stats service.
            { "{stream.followers.new}", "6" },
            { "{stream.followers.newList}", "ViewerOne, ViewerTwo" },
            { "{stream.followers.latest}", "ViewerTwo" },
            { "{stream.subs.new}", "3" },
            { "{stream.subs.resubs}", "2" },
            { "{stream.subs.gifted}", "5" },
            { "{stream.subs.total}", "10" },
            { "{stream.subs.topGifter}", "ViewerTwo" },
            { "{stream.subs.selfSubs}", "1" },
            { "{stream.bits.total}", "1,250" },
            { "{stream.bits.top}", "ViewerOne (500)" },
            { "{stream.raids.in}", "1" },
            { "{stream.raids.lastRaider}", "OtherStreamer" },
            { "{stream.raids.lastRaiderViewers}", "25" },
            { "{stream.hypetrains}", "1" },
            { "{stream.hypetrains.topLevel}", "3" },
            { "{stream.redemptions}", "14" },
            { "{stream.songRequesters}", "4" },
            { "{stream.messages}", "2,430" },
            { "{stream.chatters.unique}", "33" },
            { "{stream.commandsRun}", "88" },
            { "{stream.topChatter}", "ViewerOne (140)" },
            { "{stream.topChatters:3}", "ViewerOne, ViewerTwo, ViewerThree" },
            { "{stream.topChatters:2}", "ViewerOne, ViewerTwo" },
            { "{stream.topCommand}", "!hug (31)" },
            // The last finished stream and the stream history.
            { "{stream.last.endedAt}", "2026-10-24" },
            { "{stream.last.since}", "1 day, 16 hours" },
            { "{stream.last.relative}", "2 days ago" },
            { "{stream.last.duration}", "4h 10m" },
            { "{stream.last.game}", "Just Chatting" },
            { "{stream.last.title}", "Chill Saturday" },
            { "{stream.last.peak}", "61" },
            { "{stream.last.followers}", "9" },
            { "{stream.last.subs}", "4" },
            { "{stream.last.messages}", "3,102" },
            {
                "{stream.games.history:3}",
                "Software and Game Development, Just Chatting, Minecraft"
            },
            { "{stream.count}", "214" },
            { "{stream.count.month}", "11" },
            { "{stream.hours.month}", "46h" },
            { "{stream.number}", "stream #214" },
            // The channel.
            { "{channel.mention}", "@SampleChannel" },
            { "{channel.followers}", "1,234" },
            { "{channel.followers.compact}", "1.2k" },
            { "{channel.subs}", "80" },
            { "{channel.language}", "en" },
            { "{channel.timezone}", "Europe/Amsterdam" },
            { "{channel.localTime}", "15:30" },
            { "{channel.personality}", "Informative" },
            { "{channel.prefix}", "!" },
            { "{channel.tags}", "English, Coding" },
            { "{channel.ccls}", "" },
            { "{channel.isBranded}", "false" },
            { "{if:{channel.isBranded}|#ad|}", "" },
            { "{channel.team}", "SampleTeam" },
            { "{channel.editors.count}", "2" },
            { "{channel.mods.count}", "6" },
            { "{channel.vips.count}", "4" },
            { "{channel.vod.latest}", "https://www.twitch.tv/videos/123" },
            { "{channel.vod.latest.title}", "Chill Saturday" },
            { "{channel.raids.outCount:otherstreamer}", "3" },
            { "{channel.raids.inCount:otherstreamer}", "5" },
            { "{channel.raids.lastOut}", "OtherStreamer" },
            { "{channel.raids.lastIn.at}", "2026-10-24" },
            { "{channel.age}", "6 years, 2 months" },
            // Edge cases the catalog implies: bad arguments, unknown names, nesting.
            { "{stream.title.short:0}", "{stream.title.short:0}" },
            { "{stream.title.short:many}", "{stream.title.short:many}" },
            { "{stream.title.short}", "{stream.title.short}" },
            { "{stream.topChatters:0}", "{stream.topChatters:0}" },
            { "{channel.raids.outCount:nobody}", "0" },
            { "{channel.raids.outCount}", "{channel.raids.outCount}" },
            { "{text.upper:{stream.last.game}}", "JUST CHATTING" },
            { "{stream.subs.new} subs and {stream.subs.total} total", "3 subs and 10 total" },
        };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task The_example_renders_to_exactly_the_output_cell(
        string template,
        string expected
    )
    {
        await using StreamHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(template, StreamHelperFixture.ChannelA);

        rendered.Should().Be(expected);
    }

    public static TheoryData<string, string> OtherChannelRows =>
        new()
        {
            { "{stream.viewers.peak}", "9" },
            { "{stream.followers.new}", "1" },
            { "{stream.subs.total}", "1" },
            { "{channel.followers}", "5" },
            { "{channel.subs}", "2" },
            { "{channel.mention}", "@OtherChannel" },
            { "{channel.prefix}", "?" },
            { "{channel.personality}", "Sassy" },
            { "{channel.language}", "en-GB" },
            { "{channel.timezone}", "UTC" },
            { "{channel.localTime}", "14:30" },
            { "{channel.tags}", "Nederlands" },
            { "{channel.ccls}", "MatureGame" },
            { "{channel.isBranded}", "true" },
            { "{channel.editors.count}", "1" },
            { "{channel.mods.count}", "1" },
            { "{channel.vips.count}", "0" },
            { "{channel.age}", "1 year" },
            { "{stream.status}", "offline" },
            { "{stream.count}", "0" },
            { "{channel.raids.outCount:otherstreamer}", "0" },
            // No team, no VOD, no finished stream, no live stream: the token prints as written.
            { "{channel.team}", "{channel.team}" },
            { "{channel.vod.latest}", "{channel.vod.latest}" },
            { "{stream.last.game}", "{stream.last.game}" },
            { "{stream.uptime.long}", "{stream.uptime.long}" },
            { "{stream.followers.latest}", "{stream.followers.latest}" },
        };

    [Theory]
    [MemberData(nameof(OtherChannelRows))]
    public async Task A_second_channel_reads_its_own_data_and_never_the_first_ones(
        string template,
        string expected
    )
    {
        await using StreamHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(template, StreamHelperFixture.ChannelB);

        rendered.Should().Be(expected);
    }

    [Fact]
    public async Task A_render_without_a_channel_prints_every_channel_token_as_written()
    {
        await using StreamHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{stream.viewers.peak} {channel.followers} {stream.status}",
            null
        );

        rendered.Should().Be("{stream.viewers.peak} {channel.followers} {stream.status}");
    }
}

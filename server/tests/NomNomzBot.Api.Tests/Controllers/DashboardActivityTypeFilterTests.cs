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
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the activity window the Supporters page reads. On 2026-10-15 the Supporters page showed "Gift events 0"
/// although the journal held three <c>channel.subscription.gift</c> rows and 21 gifted <c>channel.subscribe</c>
/// rows: the feed kept only the newest 40 non-chat rows, and later redemptions and shoutouts had pushed every gift
/// out. A type filter applied BEFORE the cut keeps them. Payloads are the real journal shapes.
/// </summary>
public sealed class DashboardActivityTypeFilterTests
{
    private const string GiftPayload =
        "{\"tier\":\"1000\",\"giftCount\":10,\"actorLogin\":\"finwo_\",\"isAnonymous\":false,\"gifterUserId\":\"491902010\",\"gifterDisplayName\":\"finwo_\"}";

    private static readonly DateTime Start = new(2026, 10, 15, 20, 0, 0, DateTimeKind.Utc);

    private static List<ChannelEvent> Journal(Guid channel)
    {
        List<ChannelEvent> rows =
        [
            Row(channel, "channel.subscription.gift", GiftPayload, 0),
            Row(channel, "subscribe", "{\"user\":\"legacy_sub\"}", 1),
            Row(channel, "channel.subscribe", "{\"tier\":\"1000\",\"isGift\":true}", 2),
            Row(channel, "channel.chat.message", "{}", 3),
        ];

        for (int i = 0; i < 60; i++)
            rows.Add(
                Row(channel, "channel.channel_points_custom_reward_redemption.add", "{}", 10 + i)
            );

        return rows;
    }

    private static ChannelEvent Row(Guid channel, string type, string data, int minute) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            ChannelId = channel,
            Type = type,
            Data = data,
            CreatedAt = Start.AddMinutes(minute),
        };

    [Fact]
    public void A_type_filter_keeps_gift_and_sub_rows_that_newer_noise_would_push_out()
    {
        Guid channel = Guid.NewGuid();
        List<ChannelEvent> journal = Journal(channel);

        List<ChannelEvent> window = DashboardController
            .RecentActivityWindow(
                journal.AsQueryable(),
                channel,
                ["channel.subscription.gift", "channel.subscribe"],
                200
            )
            .ToList();

        window
            .Select(e => e.Type)
            .Should()
            .Equal("channel.subscribe", "subscribe", "channel.subscription.gift");
        window[2].Data.Should().Be(GiftPayload);
    }

    [Fact]
    public void No_filter_still_returns_the_newest_non_chat_rows_only()
    {
        Guid channel = Guid.NewGuid();

        List<ChannelEvent> window = DashboardController
            .RecentActivityWindow(Journal(channel).AsQueryable(), channel, null, 40)
            .ToList();

        window.Should().HaveCount(40);
        window
            .Should()
            .OnlyContain(e => e.Type == "channel.channel_points_custom_reward_redemption.add");
    }

    [Fact]
    public void Another_channels_rows_never_enter_the_window()
    {
        Guid channel = Guid.NewGuid();
        List<ChannelEvent> journal = Journal(Guid.NewGuid());

        DashboardController
            .RecentActivityWindow(
                journal.AsQueryable(),
                channel,
                ["channel.subscription.gift"],
                200
            )
            .Should()
            .BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  , ")]
    public void A_blank_types_query_means_no_filter(string? raw)
    {
        DashboardController.ParseActivityTypes(raw).Should().BeNull();
    }

    [Fact]
    public void A_types_query_splits_on_commas_and_trims()
    {
        DashboardController
            .ParseActivityTypes(" channel.subscribe , channel.cheer,")
            .Should()
            .Equal("channel.subscribe", "channel.cheer");
    }
}

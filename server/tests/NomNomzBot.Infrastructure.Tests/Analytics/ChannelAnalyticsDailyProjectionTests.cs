// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Linq;
using FluentAssertions;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Analytics.Entities;
using NomNomzBot.Infrastructure.Analytics;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Analytics;

/// <summary>
/// Proves the channel-daily projection (analytics.md §3.1, M.8): it folds the journal into the per-channel daily
/// aggregate (chat → messages, follow → new followers, sub/gift → new subscribers, cheer → bits, command/reward
/// → counts), ignores directory-level events with no channel, and a reset clears the read model for replay.
/// </summary>
public sealed class ChannelAnalyticsDailyProjectionTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000f01");
    private static readonly DateTime Now = new(2026, 6, 22, 12, 0, 0, DateTimeKind.Utc);
    private static int _seq;

    private static (ChannelAnalyticsDailyProjection Sut, AuthDbContext Db) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        return (new ChannelAnalyticsDailyProjection(db), db);
    }

    private static EventRecord Event(string eventType, Guid? broadcaster, string payload = "{}") =>
        new(
            ++_seq,
            Guid.NewGuid(),
            broadcaster,
            _seq,
            eventType,
            1,
            "domain",
            payload,
            false,
            null,
            null,
            null,
            null,
            null,
            "{}",
            Now,
            Now
        );

    [Fact]
    public async Task Folds_activity_into_the_per_channel_daily_aggregate()
    {
        (ChannelAnalyticsDailyProjection sut, AuthDbContext db) = Build();

        await sut.ApplyAsync(Event("ChatMessageReceivedEvent", Channel));
        await sut.ApplyAsync(Event("ChatMessageReceivedEvent", Channel));
        await sut.ApplyAsync(Event("ChatMessageReceivedEvent", Channel));
        await sut.ApplyAsync(Event("NewFollowerEvent", Channel));
        await sut.ApplyAsync(Event("NewFollowerEvent", Channel));
        await sut.ApplyAsync(Event("NewSubscriptionEvent", Channel));
        await sut.ApplyAsync(Event("GiftSubscriptionEvent", Channel));
        await sut.ApplyAsync(Event("CommandExecutedEvent", Channel));
        await sut.ApplyAsync(Event("RewardRedeemedEvent", Channel));
        await sut.ApplyAsync(Event("CheerEvent", Channel, "{\"Bits\":100}"));

        ChannelAnalyticsDaily row = db.ChannelAnalyticsDailies.Single();
        row.BroadcasterId.Should().Be(Channel);
        row.ActivityDate.Should().Be(new DateOnly(2026, 6, 22));
        row.TotalMessages.Should().Be(3);
        row.NewFollowers.Should().Be(2);
        row.NewSubscribers.Should().Be(2); // sub + gift
        row.CommandsRun.Should().Be(1);
        row.RedemptionsCount.Should().Be(1);
        row.BitsCheered.Should().Be(100);
    }

    [Fact]
    public async Task Ignores_a_directory_level_event_with_no_channel()
    {
        (ChannelAnalyticsDailyProjection sut, AuthDbContext db) = Build();

        await sut.ApplyAsync(Event("ChatMessageReceivedEvent", broadcaster: null));

        db.ChannelAnalyticsDailies.Should().BeEmpty();
    }

    [Fact]
    public async Task Reset_clears_the_aggregate_for_replay()
    {
        (ChannelAnalyticsDailyProjection sut, AuthDbContext db) = Build();
        await sut.ApplyAsync(Event("ChatMessageReceivedEvent", Channel));
        db.ChannelAnalyticsDailies.Should().ContainSingle();

        await sut.ResetAsync(Channel);

        db.ChannelAnalyticsDailies.Should().BeEmpty();
    }
}

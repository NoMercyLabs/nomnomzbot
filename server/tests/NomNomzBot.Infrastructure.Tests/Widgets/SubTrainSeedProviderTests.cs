// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Proves the sub_train widget gets its train back on join: the subs still inside the window, a gift counted by its
/// gift count once (never again through its gifted-sub rows), each frame stamped with when it really happened so the
/// widget can work out the time left, and never another channel's rows.
/// </summary>
public sealed class SubTrainSeedProviderTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-0000000000f1");
    private static readonly Guid OtherBroadcaster = Guid.Parse(
        "0192b000-0000-7000-8000-0000000000f2"
    );
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    private static int _next;

    private static ChannelEvent Row(Guid channelId, string type, int secondsAgo, string data = "{}")
    {
        DateTime at = Now.AddSeconds(-secondsAgo);
        JsonObject body = JsonNode.Parse(data)!.AsObject();
        body["occurredAt"] = at.ToString("O");
        return new()
        {
            Id = $"evt-{++_next}",
            ChannelId = channelId,
            Type = type,
            Data = body.ToJsonString(),
            CreatedAt = at,
        };
    }

    private static Widget Train(Dictionary<string, object> settings) =>
        new() { BroadcasterId = Broadcaster, Settings = settings };

    private static SubTrainSeedProvider Sut(WidgetTestDbContext ctx) =>
        new(ctx, new FakeTimeProvider(new DateTimeOffset(Now)));

    private static int Total(IReadOnlyList<WidgetSeedFrame> frames) =>
        frames.Sum(f =>
            f.EventType == "gift"
                ? JsonSerializer.SerializeToElement(f.Data).GetProperty("count").GetInt32()
                : 1
        );

    [Fact]
    public async Task Seed_rebuilds_the_train_from_in_window_subs_counting_a_gift_once()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        await using (WidgetTestDbContext ctx = database.NewContext())
        {
            ctx.ChannelEvents.AddRange(
                // Outside the 5 minute window: never counted.
                Row(Broadcaster, "channel.subscribe", 400),
                Row(Broadcaster, "channel.subscription.gift", 301, "{\"giftCount\":9}"),
                // Inside the window.
                Row(Broadcaster, "channel.subscribe", 250, "{\"isGift\":false}"),
                Row(Broadcaster, "channel.subscription.message", 200),
                // A gift of 5, its 5 gifted subs and 5 received rows: counts 5, not 10 or 15.
                Row(Broadcaster, "channel.subscription.gift", 100, "{\"giftCount\":5}"),
                Row(Broadcaster, "channel.subscribe", 100, "{\"isGift\":true}"),
                Row(Broadcaster, "channel.subscribe", 100, "{\"isGift\":true}"),
                Row(Broadcaster, "channel.subscribe", 100, "{\"isGift\":true}"),
                Row(Broadcaster, "channel.subscribe", 100, "{\"isGift\":true}"),
                Row(Broadcaster, "channel.subscribe", 100, "{\"isGift\":true}"),
                Row(Broadcaster, "channel.subscription.gift.received", 100),
                Row(Broadcaster, "channel.subscription.gift.received", 100),
                // An anonymous gift of 2.
                Row(
                    Broadcaster,
                    "channel.subscription.gift",
                    30,
                    "{\"giftCount\":2,\"isAnonymous\":true}"
                ),
                // A follow is not a sub.
                Row(Broadcaster, "channel.follow", 20),
                // Another channel's rows.
                Row(OtherBroadcaster, "channel.subscribe", 10),
                Row(OtherBroadcaster, "channel.subscription.gift", 10, "{\"giftCount\":50}")
            );
            await ctx.SaveChangesAsync();
        }

        await using WidgetTestDbContext readCtx = database.NewContext();
        SubTrainSeedProvider sut = Sut(readCtx);

        IReadOnlyList<WidgetSeedFrame> frames = await sut.SeedAsync(
            Broadcaster,
            Train(new Dictionary<string, object> { ["windowMs"] = 300000L }),
            CancellationToken.None
        );

        Total(frames).Should().Be(1 + 1 + 5 + 2);
        frames.Select(f => f.EventType).Should().Equal("subscription", "resub", "gift", "gift");
        frames
            .Select(f => f.OccurredAt.UtcDateTime)
            .Should()
            .Equal(
                Now.AddSeconds(-250),
                Now.AddSeconds(-200),
                Now.AddSeconds(-100),
                Now.AddSeconds(-30)
            );
        frames[^1].OccurredAt.UtcDateTime.Add(Window).Should().Be(Now.AddSeconds(270));
        sut.NaturalKey.Should().Be("sub_train");
    }

    [Fact]
    public async Task Seed_uses_the_window_from_the_widget_settings()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        await using (WidgetTestDbContext ctx = database.NewContext())
        {
            ctx.ChannelEvents.AddRange(
                Row(Broadcaster, "channel.subscribe", 90),
                Row(Broadcaster, "channel.subscribe", 30)
            );
            await ctx.SaveChangesAsync();
        }

        await using WidgetTestDbContext readCtx = database.NewContext();

        IReadOnlyList<WidgetSeedFrame> frames = await Sut(readCtx)
            .SeedAsync(
                Broadcaster,
                Train(new Dictionary<string, object> { ["windowMs"] = 60000L }),
                CancellationToken.None
            );

        frames.Should().ContainSingle();
        frames[0].OccurredAt.UtcDateTime.Should().Be(Now.AddSeconds(-30));
    }

    [Fact]
    public async Task Seed_gives_no_frame_when_the_train_has_stopped()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        await using (WidgetTestDbContext ctx = database.NewContext())
        {
            ctx.ChannelEvents.Add(Row(Broadcaster, "channel.subscribe", 900));
            await ctx.SaveChangesAsync();
        }

        await using WidgetTestDbContext readCtx = database.NewContext();

        IReadOnlyList<WidgetSeedFrame> frames = await Sut(readCtx)
            .SeedAsync(
                Broadcaster,
                Train(new Dictionary<string, object>()),
                CancellationToken.None
            );

        frames.Should().BeEmpty();
    }
}

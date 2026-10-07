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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Widgets;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Proves the labels widget shows its real value after an overlay reload. Each mode seeds from its true source:
/// the follower and sub counts from Twitch as an absolute <c>count</c> frame, the latest follower and sub from this
/// channel's newest stored event in the live alert shape, the top cheerer from the stored cheers. A failed Twitch
/// call gives no frame, never a 0.
/// </summary>
public sealed class LabelsSeedProviderTests : IDisposable
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-0000000000f1");
    private static readonly Guid OtherBroadcaster = Guid.Parse(
        "0192b000-0000-7000-8000-0000000000f2"
    );
    private static readonly DateTimeOffset WentLive = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);
    private static int _next;

    private readonly ITwitchChannelsApi _channels = Substitute.For<ITwitchChannelsApi>();
    private readonly ITwitchSubscriptionsApi _subscriptions =
        Substitute.For<ITwitchSubscriptionsApi>();
    private readonly IChannelRegistry _registry = Substitute.For<IChannelRegistry>();
    private readonly WidgetSqliteTestDatabase _database = WidgetSqliteTestDatabase.Open();
    private readonly WidgetTestDbContext _ctx;

    public LabelsSeedProviderTests() => _ctx = _database.NewContext();

    public void Dispose()
    {
        _ctx.Dispose();
        _database.Dispose();
    }

    private LabelsSeedProvider Sut(DateTimeOffset? wentLiveAt = null)
    {
        _registry
            .Get(Broadcaster)
            .Returns(
                new ChannelContext
                {
                    BroadcasterId = Broadcaster,
                    TwitchChannelId = "1",
                    ChannelName = "c",
                    WentLiveAt = wentLiveAt,
                }
            );
        return new(_channels, _subscriptions, _ctx, _registry);
    }

    private static Widget Labels(string label) =>
        new()
        {
            BroadcasterId = Broadcaster,
            Settings = new() { ["label"] = label },
        };

    private static JsonElement Body(WidgetSeedFrame frame) =>
        JsonSerializer.SerializeToElement(frame.Data, Wire);

    private static ChannelEvent Row(
        Guid channelId,
        string type,
        JsonObject data,
        DateTime createdAt
    ) =>
        new()
        {
            Id = $"label-{++_next}",
            ChannelId = channelId,
            Type = type,
            Data = data.ToJsonString(),
            CreatedAt = createdAt,
        };

    private static JsonObject FollowData(string name, string id, string login) =>
        new()
        {
            ["user"] = name,
            ["user.id"] = id,
            ["user.name"] = login,
            ["followed_at"] = "2026-10-07T09:00:00.0000000+00:00",
        };

    private async Task AddRows(params ChannelEvent[] rows)
    {
        _ctx.ChannelEvents.AddRange(rows);
        await _ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Follower_count_seeds_the_twitch_total_as_an_absolute_count_frame()
    {
        _channels
            .GetChannelFollowerCountAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Result.Success(1234));

        IReadOnlyList<WidgetSeedFrame> frames = await Sut()
            .SeedAsync(Broadcaster, Labels("follower_count"), CancellationToken.None);

        frames.Should().ContainSingle();
        frames[0].EventType.Should().Be("count");
        frames[0].Data.Should().Be(new CountWidgetEventPayload("followers", 1234));
    }

    [Fact]
    public async Task Sub_count_seeds_the_twitch_total_as_an_absolute_count_frame()
    {
        _subscriptions
            .GetSubscriberCountAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Result.Success(56));

        IReadOnlyList<WidgetSeedFrame> frames = await Sut()
            .SeedAsync(Broadcaster, Labels("sub_count"), CancellationToken.None);

        frames.Should().ContainSingle();
        frames[0].EventType.Should().Be("count");
        frames[0].Data.Should().Be(new CountWidgetEventPayload("subs", 56));
    }

    [Theory]
    [InlineData("follower_count")]
    [InlineData("sub_count")]
    public async Task A_failed_twitch_call_gives_no_frame_never_a_zero(string label)
    {
        _channels
            .GetChannelFollowerCountAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<int>("missing scope", TwitchErrorCodes.MissingScope));
        _subscriptions
            .GetSubscriberCountAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<int>("missing scope", TwitchErrorCodes.MissingScope));

        IReadOnlyList<WidgetSeedFrame> frames = await Sut()
            .SeedAsync(Broadcaster, Labels(label), CancellationToken.None);

        frames.Should().BeEmpty();
    }

    [Fact]
    public async Task Latest_follower_seeds_the_newest_own_follow_row_in_the_live_follow_shape()
    {
        await AddRows(
            Row(
                Broadcaster,
                "channel.follow",
                FollowData("OldOne", "11", "oldone"),
                new(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc)
            ),
            Row(
                Broadcaster,
                "channel.follow",
                FollowData("NewestNel", "22", "newestnel"),
                new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc)
            ),
            Row(
                OtherBroadcaster,
                "channel.follow",
                FollowData("Stranger", "33", "stranger"),
                new(2026, 10, 7, 9, 30, 0, DateTimeKind.Utc)
            )
        );

        IReadOnlyList<WidgetSeedFrame> frames = await Sut()
            .SeedAsync(Broadcaster, Labels("latest_follower"), CancellationToken.None);

        frames.Should().ContainSingle();
        frames[0].EventType.Should().Be("follow");
        JsonElement body = Body(frames[0]);
        body.GetProperty("displayName").GetString().Should().Be("NewestNel");
        body.GetProperty("userId").GetString().Should().Be("22");
        body.GetProperty("login").GetString().Should().Be("newestnel");
    }

    [Fact]
    public async Task Latest_follower_without_any_follow_row_gives_no_frame()
    {
        await AddRows(
            Row(
                OtherBroadcaster,
                "channel.follow",
                FollowData("Stranger", "33", "stranger"),
                new(2026, 10, 7, 9, 30, 0, DateTimeKind.Utc)
            )
        );

        IReadOnlyList<WidgetSeedFrame> frames = await Sut()
            .SeedAsync(Broadcaster, Labels("latest_follower"), CancellationToken.None);

        frames.Should().BeEmpty();
    }

    [Fact]
    public async Task Latest_sub_seeds_a_new_sub_row_as_a_subscription_frame()
    {
        await AddRows(
            Row(
                Broadcaster,
                "channel.subscription.message",
                new()
                {
                    ["user"] = "OldResubber",
                    ["user.id"] = "5",
                    ["tier"] = "1",
                    ["months"] = "3",
                    ["streak"] = "2",
                    ["message"] = "hi",
                },
                new(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc)
            ),
            Row(
                Broadcaster,
                "channel.subscribe",
                new()
                {
                    ["user"] = "FreshSub",
                    ["user.id"] = "6",
                    ["tier"] = "2",
                },
                new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc)
            )
        );

        IReadOnlyList<WidgetSeedFrame> frames = await Sut()
            .SeedAsync(Broadcaster, Labels("latest_sub"), CancellationToken.None);

        frames.Should().ContainSingle();
        frames[0].EventType.Should().Be("subscription");
        JsonElement body = Body(frames[0]);
        body.GetProperty("displayName").GetString().Should().Be("FreshSub");
        body.GetProperty("userId").GetString().Should().Be("6");
        body.GetProperty("tier").GetString().Should().Be("2");
    }

    [Fact]
    public async Task Latest_sub_seeds_a_newer_resub_row_as_a_resub_frame()
    {
        await AddRows(
            Row(
                Broadcaster,
                "channel.subscribe",
                new()
                {
                    ["user"] = "FreshSub",
                    ["user.id"] = "6",
                    ["tier"] = "1",
                },
                new(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc)
            ),
            Row(
                Broadcaster,
                "channel.subscription.message",
                new()
                {
                    ["user"] = "LoyalLou",
                    ["user.id"] = "7",
                    ["tier"] = "3",
                    ["months"] = "14",
                    ["streak"] = "9",
                    ["message"] = "still here",
                },
                new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc)
            )
        );

        IReadOnlyList<WidgetSeedFrame> frames = await Sut()
            .SeedAsync(Broadcaster, Labels("latest_sub"), CancellationToken.None);

        frames.Should().ContainSingle();
        frames[0].EventType.Should().Be("resub");
        JsonElement body = Body(frames[0]);
        body.GetProperty("displayName").GetString().Should().Be("LoyalLou");
        body.GetProperty("tier").GetString().Should().Be("3");
        body.GetProperty("months").GetInt32().Should().Be(14);
        body.GetProperty("streak").GetInt32().Should().Be(9);
        body.GetProperty("message").GetString().Should().Be("still here");
    }

    [Fact]
    public async Task Top_cheerer_seeds_the_streams_cheer_board_per_user()
    {
        await AddRows(
            Cheer("Ann", 100, WentLive.AddMinutes(5)),
            Cheer("Ann", 50, WentLive.AddMinutes(10)),
            Cheer("Bob", 120, WentLive.AddMinutes(20)),
            Cheer("Cyd", 9999, WentLive.AddHours(-2))
        );

        IReadOnlyList<WidgetSeedFrame> frames = await Sut(WentLive)
            .SeedAsync(Broadcaster, Labels("top_cheerer"), CancellationToken.None);

        frames.Should().OnlyContain(f => f.EventType == "cheer");
        frames
            .ToDictionary(
                f => Body(f).GetProperty("displayName").GetString()!,
                f => Body(f).GetProperty("bits").GetInt32()
            )
            .Should()
            .BeEquivalentTo(new Dictionary<string, int> { ["Ann"] = 150, ["Bob"] = 120 });
    }

    [Fact]
    public async Task Top_cheerer_while_offline_gives_no_frames()
    {
        await AddRows(Cheer("Ann", 100, WentLive.AddMinutes(5)));

        IReadOnlyList<WidgetSeedFrame> frames = await Sut(null)
            .SeedAsync(Broadcaster, Labels("top_cheerer"), CancellationToken.None);

        frames.Should().BeEmpty();
    }

    private static ChannelEvent Cheer(string user, int bits, DateTimeOffset at) =>
        Row(
            Broadcaster,
            "channel.cheer",
            new()
            {
                ["user"] = user,
                ["bits"] = bits.ToString(),
                ["anonymous"] = "false",
                ["occurredAt"] = at.ToString("O"),
            },
            new(2026, 10, 7, 11, 0, 0, DateTimeKind.Utc)
        );
}

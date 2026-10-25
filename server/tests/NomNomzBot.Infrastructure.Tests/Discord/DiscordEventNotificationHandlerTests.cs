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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Domain.Discord;
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Infrastructure.Discord.EventHandlers;
using NomNomzBot.Infrastructure.Platform.Security;

namespace NomNomzBot.Infrastructure.Tests.Discord;

/// <summary>
/// Proves the subscription, gift subscription, cheer, raid and stream end Discord triggers. Each one raises
/// the real domain event against the real dispatcher and asserts what reached Discord (channel, rendered
/// text) and what the dispatch log holds (trigger, dedupe key, status). Follow has its own test class.
/// </summary>
public sealed class DiscordEventNotificationHandlerTests
{
    private static readonly DateTimeOffset At = new(2026, 6, 22, 20, 0, 0, TimeSpan.Zero);

    private static OutboundSanctionAccessor Sanctions() => new();

    private static DiscordSubscriptionNotificationHandler Subscription(
        IDiscordNotificationDispatcher dispatcher
    ) => new(dispatcher, Sanctions(), NullLogger<DiscordSubscriptionNotificationHandler>.Instance);

    private static DiscordGiftSubscriptionNotificationHandler Gift(
        IDiscordNotificationDispatcher dispatcher
    ) =>
        new(
            dispatcher,
            Sanctions(),
            NullLogger<DiscordGiftSubscriptionNotificationHandler>.Instance
        );

    private static DiscordCheerNotificationHandler Cheer(
        IDiscordNotificationDispatcher dispatcher
    ) => new(dispatcher, Sanctions(), NullLogger<DiscordCheerNotificationHandler>.Instance);

    private static DiscordRaidNotificationHandler Raid(IDiscordNotificationDispatcher dispatcher) =>
        new(dispatcher, Sanctions(), NullLogger<DiscordRaidNotificationHandler>.Instance);

    private static DiscordStreamEndNotificationHandler StreamEnd(
        IDiscordNotificationDispatcher dispatcher
    ) => new(dispatcher, Sanctions(), NullLogger<DiscordStreamEndNotificationHandler>.Instance);

    private static NewSubscriptionEvent Sub(Guid broadcaster, bool isGift = false) =>
        new()
        {
            BroadcasterId = broadcaster,
            OccurredAt = At,
            UserId = "7",
            UserDisplayName = "Sub Person",
            Tier = "2000",
            IsGift = isGift,
        };

    private static GiftSubscriptionEvent GiftBatch(Guid broadcaster, bool anonymous = false) =>
        new()
        {
            BroadcasterId = broadcaster,
            OccurredAt = At,
            GifterUserId = "8",
            GifterDisplayName = "Generous",
            Tier = "1000",
            GiftCount = 5,
            IsAnonymous = anonymous,
            Recipients = [],
        };

    private static CheerEvent CheerOf(Guid broadcaster, bool anonymous = false) =>
        new()
        {
            BroadcasterId = broadcaster,
            OccurredAt = At,
            UserId = "9",
            UserDisplayName = "Cheerer",
            Bits = 300,
            Message = "pog",
            IsAnonymous = anonymous,
        };

    private static RaidEvent RaidOf(Guid broadcaster) =>
        new()
        {
            BroadcasterId = broadcaster,
            OccurredAt = At,
            FromUserId = "10",
            FromDisplayName = "Raider",
            FromLogin = "raider",
            ViewerCount = 120,
        };

    private static ChannelOfflineEvent Offline(
        Guid broadcaster,
        TimeSpan duration,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? occurredAt = null
    ) =>
        new()
        {
            BroadcasterId = broadcaster,
            OccurredAt = occurredAt ?? At,
            Provider = "twitch",
            BroadcasterDisplayName = "Stoney",
            StreamDuration = duration,
            StreamStartedAt = startedAt,
        };

    private static async Task<DiscordTriggerHandlerHarness> RuleAsync(
        string trigger,
        string template
    ) => await DiscordTriggerHandlerHarness.WithRuleAsync(trigger, template);

    private static async Task AssertOnePostAsync(
        DiscordTriggerHandlerHarness harness,
        string trigger,
        string text,
        string dedupeKey
    )
    {
        harness.Gateway.Posts.Should().ContainSingle();
        harness.Gateway.Posts[0].ChannelId.Should().Be(DiscordTriggerHandlerHarness.DiscordChannel);
        harness.Gateway.Posts[0].Message.Content.Should().Be(text);
        DiscordNotificationDispatch row = (await harness.DispatchesAsync()).Single();
        row.TriggerType.Should().Be(trigger);
        row.Status.Should().Be("sent");
        row.DedupeKey.Should().Be(dedupeKey);
    }

    // ── subscription ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Subscription_PostsTheRule_WithNameAndTier()
    {
        using DiscordTriggerHandlerHarness harness = await RuleAsync(
            "subscription",
            "{user} subscribed ({tier})"
        );

        await harness.RaiseAsync(Subscription, Sub(harness.BroadcasterId));

        await AssertOnePostAsync(
            harness,
            "subscription",
            "Sub Person subscribed (Tier 2)",
            $"subscription:twitch:7:{At.UtcDateTime:O}"
        );
    }

    [Fact]
    public async Task Subscription_DeliveredTwice_PostsOnce()
    {
        using DiscordTriggerHandlerHarness harness = await RuleAsync("subscription", "sub!");

        await harness.RaiseAsync(Subscription, Sub(harness.BroadcasterId));
        await harness.RaiseAsync(Subscription, Sub(harness.BroadcasterId));

        harness.Gateway.Posts.Should().ContainSingle();
        (await harness.DispatchesAsync())
            .Select(row => row.Status)
            .Should()
            .Equal("sent", "skipped_dupe");
    }

    [Fact]
    public async Task Subscription_ThatWasGifted_IsLeftToTheGiftTrigger()
    {
        using DiscordTriggerHandlerHarness harness = await RuleAsync("subscription", "sub!");

        await harness.RaiseAsync(Subscription, Sub(harness.BroadcasterId, isGift: true));

        harness.Gateway.Posts.Should().BeEmpty();
        (await harness.DispatchesAsync()).Should().BeEmpty();
    }

    // ── gift subscription ────────────────────────────────────────────────────

    [Fact]
    public async Task Gift_PostsOnePerBatch_WithGifterCountAndTier()
    {
        using DiscordTriggerHandlerHarness harness = await RuleAsync(
            "gift_subscription",
            "{user} gifted {count} ({tier})"
        );

        await harness.RaiseAsync(Gift, GiftBatch(harness.BroadcasterId));

        await AssertOnePostAsync(
            harness,
            "gift_subscription",
            "Generous gifted 5 (Tier 1)",
            $"gift_subscription:twitch:8:{At.UtcDateTime:O}:5"
        );
    }

    [Fact]
    public async Task Gift_FromAnAnonymousGifter_NamesNoOne()
    {
        using DiscordTriggerHandlerHarness harness = await RuleAsync(
            "gift_subscription",
            "{user} gifted {count}"
        );

        await harness.RaiseAsync(Gift, GiftBatch(harness.BroadcasterId, anonymous: true));

        harness.Gateway.Posts.Single().Message.Content.Should().Be("Anonymous gifted 5");
    }

    [Fact]
    public async Task Gift_DeliveredTwice_PostsOnce()
    {
        using DiscordTriggerHandlerHarness harness = await RuleAsync("gift_subscription", "gift!");

        await harness.RaiseAsync(Gift, GiftBatch(harness.BroadcasterId));
        await harness.RaiseAsync(Gift, GiftBatch(harness.BroadcasterId));

        harness.Gateway.Posts.Should().ContainSingle();
    }

    // ── cheer ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cheer_PostsTheRule_WithNameBitsAndMessage()
    {
        using DiscordTriggerHandlerHarness harness = await RuleAsync(
            "cheer",
            "{user}: {bits} bits - {message}"
        );

        await harness.RaiseAsync(Cheer, CheerOf(harness.BroadcasterId));

        await AssertOnePostAsync(
            harness,
            "cheer",
            "Cheerer: 300 bits - pog",
            $"cheer:twitch:9:{At.UtcDateTime:O}:300"
        );
    }

    [Fact]
    public async Task Cheer_FromAnAnonymousViewer_NamesNoOne()
    {
        using DiscordTriggerHandlerHarness harness = await RuleAsync("cheer", "{user} {bits}");

        await harness.RaiseAsync(Cheer, CheerOf(harness.BroadcasterId, anonymous: true));

        harness.Gateway.Posts.Single().Message.Content.Should().Be("Anonymous 300");
    }

    [Fact]
    public async Task Cheer_DeliveredTwice_PostsOnce()
    {
        using DiscordTriggerHandlerHarness harness = await RuleAsync("cheer", "cheer!");

        await harness.RaiseAsync(Cheer, CheerOf(harness.BroadcasterId));
        await harness.RaiseAsync(Cheer, CheerOf(harness.BroadcasterId));

        harness.Gateway.Posts.Should().ContainSingle();
    }

    // ── raid ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Raid_PostsTheRule_WithRaiderAndViewerCount()
    {
        using DiscordTriggerHandlerHarness harness = await RuleAsync(
            "raid",
            "{user} raided with {viewers}"
        );

        await harness.RaiseAsync(Raid, RaidOf(harness.BroadcasterId));

        await AssertOnePostAsync(
            harness,
            "raid",
            "Raider raided with 120",
            $"raid:10:{At.UtcDateTime:O}"
        );
    }

    [Fact]
    public async Task Raid_DeliveredTwice_PostsOnce()
    {
        using DiscordTriggerHandlerHarness harness = await RuleAsync("raid", "raid!");

        await harness.RaiseAsync(Raid, RaidOf(harness.BroadcasterId));
        await harness.RaiseAsync(Raid, RaidOf(harness.BroadcasterId));

        harness.Gateway.Posts.Should().ContainSingle();
    }

    // ── stream end ───────────────────────────────────────────────────────────

    [Fact]
    public async Task StreamEnd_PostsTheRule_WithTheStreamLength()
    {
        using DiscordTriggerHandlerHarness harness = await RuleAsync(
            "stream_end",
            "{broadcaster} ended after {duration}"
        );

        await harness.RaiseAsync(StreamEnd, Offline(harness.BroadcasterId, new(2, 5, 0)));

        await AssertOnePostAsync(
            harness,
            "stream_end",
            "Stoney ended after 2h 5m",
            "stream_end:twitch:202606222000"
        );
    }

    [Fact]
    public async Task StreamEnd_WithAnUnknownLength_SaysUnknownNotZeroMinutes()
    {
        using DiscordTriggerHandlerHarness harness = await RuleAsync(
            "stream_end",
            "ran {duration}"
        );

        await harness.RaiseAsync(StreamEnd, Offline(harness.BroadcasterId, TimeSpan.Zero));

        harness.Gateway.Posts.Single().Message.Content.Should().Be("ran unknown");
    }

    [Fact]
    public async Task StreamEnd_UnderAnHour_ShowsMinutesOnly()
    {
        using DiscordTriggerHandlerHarness harness = await RuleAsync(
            "stream_end",
            "ran {duration}"
        );

        await harness.RaiseAsync(StreamEnd, Offline(harness.BroadcasterId, new(0, 42, 10)));

        harness.Gateway.Posts.Single().Message.Content.Should().Be("ran 42m");
    }

    [Fact]
    public async Task StreamEnd_DeliveredTwice_PostsOnce()
    {
        using DiscordTriggerHandlerHarness harness = await RuleAsync("stream_end", "bye");

        await harness.RaiseAsync(StreamEnd, Offline(harness.BroadcasterId, new(1, 0, 0)));
        await harness.RaiseAsync(StreamEnd, Offline(harness.BroadcasterId, new(1, 0, 0)));

        harness.Gateway.Posts.Should().ContainSingle();
    }

    [Fact]
    public async Task StreamEnd_SameStreamReportedOfflineInDifferentMinutes_PostsOnce()
    {
        using DiscordTriggerHandlerHarness harness = await RuleAsync("stream_end", "bye");
        DateTimeOffset startedAt = At.AddHours(-2);

        await harness.RaiseAsync(
            StreamEnd,
            Offline(harness.BroadcasterId, new(2, 0, 0), startedAt, At.AddSeconds(50))
        );
        await harness.RaiseAsync(
            StreamEnd,
            Offline(harness.BroadcasterId, new(2, 0, 0), startedAt, At.AddSeconds(70))
        );

        harness.Gateway.Posts.Should().ContainSingle();
        List<DiscordNotificationDispatch> rows = await harness.DispatchesAsync();
        rows.Select(row => row.Status).Should().Equal("sent", "skipped_dupe");
        rows[0].DedupeKey.Should().Be($"stream_end:twitch:{startedAt.UtcDateTime:O}");
        rows[1]
            .DedupeKey.Should()
            .StartWith(rows[0].DedupeKey, "the repeat is logged under the same stream key");
    }

    [Fact]
    public async Task StreamEnd_TwoDifferentStreams_PostTwice()
    {
        using DiscordTriggerHandlerHarness harness = await RuleAsync("stream_end", "bye");

        await harness.RaiseAsync(
            StreamEnd,
            Offline(harness.BroadcasterId, new(1, 0, 0), At.AddHours(-9), At.AddHours(-8))
        );
        await harness.RaiseAsync(
            StreamEnd,
            Offline(harness.BroadcasterId, new(1, 0, 0), At.AddHours(-2), At.AddHours(-1))
        );

        harness.Gateway.Posts.Should().HaveCount(2);
    }

    // ── no rule, no post ─────────────────────────────────────────────────────

    [Fact]
    public async Task WithoutARuleForTheTrigger_NothingIsPosted()
    {
        using DiscordTriggerHandlerHarness harness = await RuleAsync("go_live", "live!");

        await harness.RaiseAsync(Subscription, Sub(harness.BroadcasterId));
        await harness.RaiseAsync(Gift, GiftBatch(harness.BroadcasterId));
        await harness.RaiseAsync(Cheer, CheerOf(harness.BroadcasterId));
        await harness.RaiseAsync(Raid, RaidOf(harness.BroadcasterId));
        await harness.RaiseAsync(StreamEnd, Offline(harness.BroadcasterId, new(1, 0, 0)));

        harness.Gateway.Posts.Should().BeEmpty();
        (await harness.DispatchesAsync()).Should().BeEmpty();
    }

    // ── catalog ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("subscription", "user", "tier", "channel.display")]
    [InlineData("gift_subscription", "user", "count", "tier", "channel.display")]
    [InlineData("cheer", "user", "bits", "message", "channel.display")]
    [InlineData("raid", "user", "viewers", "channel.display")]
    [InlineData("stream_end", "broadcaster", "duration", "channel.name")]
    public void EachTrigger_IsInTheCatalog_WithItsLabelAndPlaceholders(
        string name,
        params string[] placeholders
    )
    {
        DiscordNotificationTrigger trigger =
            DiscordConfigDefaults.NotificationTriggerCatalog.Single(entry => entry.Name == name);

        trigger.LabelKey.Should().Be($"dashboard.discord.triggers.{name}");
        trigger.Placeholders.Should().Equal(placeholders);
    }
}

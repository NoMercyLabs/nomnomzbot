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
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.DTOs.Twitch.EventSub;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Infrastructure.Platform.Eventing.Translators;
using NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing.Translators;

/// <summary>
/// Behaviour tests for the subscription/cheer fan-out translators. Each runs a realistic raw EventSub
/// <c>event</c> payload through its translator with a capturing bus and a deterministic clock, then asserts
/// the published event's concrete type and parsed field values — including the anonymous edge cases where
/// Twitch nulls the user fields.
/// </summary>
public sealed class SubscriptionTranslatorsTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)
    );

    private static EventSubNotification Notification(Guid tenant, string type, string payload)
    {
        using JsonDocument doc = JsonDocument.Parse(payload);
        return new EventSubNotification
        {
            MessageId = "msg-1",
            MessageTimestamp = new DateTimeOffset(2026, 6, 20, 11, 30, 0, TimeSpan.Zero),
            SubscriptionType = type,
            SubscriptionVersion = "1",
            BroadcasterId = tenant,
            TwitchBroadcasterUserId = "broadcaster-99",
            Event = doc.RootElement.Clone(),
        };
    }

    [Fact]
    public async Task ChannelSubscribe_PublishesNewSubscriptionEvent_WithParsedFields()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelSubscribeTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.subscribe",
                """
                {
                    "user_id": "1234",
                    "user_login": "cool_user",
                    "user_name": "Cool_User",
                    "broadcaster_user_id": "broadcaster-99",
                    "tier": "1000",
                    "is_gift": false
                }
                """
            )
        );

        NewSubscriptionEvent published = bus.EventsOf<NewSubscriptionEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.UserId.Should().Be("1234");
        published.UserDisplayName.Should().Be("Cool_User");
        published.Tier.Should().Be("1000");
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task ChannelSubscriptionMessage_PublishesResubscriptionEvent_WithNestedMessageText()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelSubscriptionMessageTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.subscription.message",
                """
                {
                    "user_id": "1234",
                    "user_login": "cool_user",
                    "user_name": "Cool_User",
                    "tier": "1000",
                    "cumulative_months": 15,
                    "streak_months": 3,
                    "duration_months": 6,
                    "message": { "text": "Love the stream!", "emotes": [] }
                }
                """
            )
        );

        ResubscriptionEvent published = bus.EventsOf<ResubscriptionEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.UserId.Should().Be("1234");
        published.UserDisplayName.Should().Be("Cool_User");
        published.Tier.Should().Be("1000");
        published.CumulativeMonths.Should().Be(15);
        published.StreakMonths.Should().Be(3);
        published.Message.Should().Be("Love the stream!");
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task ChannelSubscriptionMessage_WithoutMessageObject_HasNullMessage()
    {
        CapturingEventBus bus = new();
        ChannelSubscriptionMessageTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                Guid.NewGuid(),
                "channel.subscription.message",
                """
                {
                    "user_id": "1234",
                    "user_name": "Cool_User",
                    "tier": "2000",
                    "cumulative_months": 1,
                    "streak_months": 1
                }
                """
            )
        );

        ResubscriptionEvent published = bus.EventsOf<ResubscriptionEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published
            .Message.Should()
            .BeNull("an absent message object degrades to null, never throws");
        published.Tier.Should().Be("2000");
    }

    [Fact]
    public async Task ChannelSubscriptionGift_PublishesGiftSubscriptionEvent_WithGifterAndTotal()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelSubscriptionGiftTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.subscription.gift",
                """
                {
                    "user_id": "1234",
                    "user_login": "cool_user",
                    "user_name": "Cool_User",
                    "tier": "1000",
                    "total": 5,
                    "cumulative_total": 50,
                    "is_anonymous": false
                }
                """
            )
        );

        GiftSubscriptionEvent published = bus.EventsOf<GiftSubscriptionEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.GifterUserId.Should().Be("1234");
        published.GifterDisplayName.Should().Be("Cool_User");
        published.Tier.Should().Be("1000");
        published.GiftCount.Should().Be(5);
        published.IsAnonymous.Should().BeFalse();
        published.Recipients.Should().BeEmpty("the gift event does not enumerate recipients");
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task ChannelSubscriptionGift_Anonymous_HasEmptyGifterFields()
    {
        CapturingEventBus bus = new();
        ChannelSubscriptionGiftTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                Guid.NewGuid(),
                "channel.subscription.gift",
                """
                {
                    "user_id": null,
                    "user_login": null,
                    "user_name": null,
                    "tier": "1000",
                    "total": 2,
                    "is_anonymous": true
                }
                """
            )
        );

        GiftSubscriptionEvent published = bus.EventsOf<GiftSubscriptionEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.IsAnonymous.Should().BeTrue();
        published
            .GifterUserId.Should()
            .BeEmpty("an anonymous gifter has no user_id → empty string");
        published.GifterDisplayName.Should().BeEmpty();
        published.GiftCount.Should().Be(2);
    }

    [Fact]
    public async Task ChannelSubscriptionEnd_PublishesSubscriptionEndedEvent_WithParsedFields()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelSubscriptionEndTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.subscription.end",
                """
                {
                    "user_id": "1234",
                    "user_login": "cool_user",
                    "user_name": "Cool_User",
                    "tier": "3000",
                    "is_gift": true
                }
                """
            )
        );

        SubscriptionEndedEvent published = bus.EventsOf<SubscriptionEndedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.UserId.Should().Be("1234");
        published.UserLogin.Should().Be("cool_user");
        published.UserDisplayName.Should().Be("Cool_User");
        published.Tier.Should().Be("3000");
        published.IsGift.Should().BeTrue();
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task ChannelCheer_PublishesCheerEvent_WithParsedFields()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelCheerTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.cheer",
                """
                {
                    "is_anonymous": false,
                    "user_id": "1234",
                    "user_login": "cool_user",
                    "user_name": "Cool_User",
                    "broadcaster_user_id": "broadcaster-99",
                    "message": "cheer100 nice stream",
                    "bits": 100
                }
                """
            )
        );

        CheerEvent published = bus.EventsOf<CheerEvent>().Should().ContainSingle().Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.UserId.Should().Be("1234");
        published.UserDisplayName.Should().Be("Cool_User");
        published.Bits.Should().Be(100);
        published.Message.Should().Be("cheer100 nice stream");
        published.IsAnonymous.Should().BeFalse();
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task ChannelCheer_Anonymous_HasEmptyUserFields()
    {
        CapturingEventBus bus = new();
        ChannelCheerTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                Guid.NewGuid(),
                "channel.cheer",
                """
                {
                    "is_anonymous": true,
                    "user_id": null,
                    "user_login": null,
                    "user_name": null,
                    "message": "cheer50",
                    "bits": 50
                }
                """
            )
        );

        CheerEvent published = bus.EventsOf<CheerEvent>().Should().ContainSingle().Subject;
        published.IsAnonymous.Should().BeTrue();
        published.UserId.Should().BeEmpty("an anonymous cheer has no user_id → empty string");
        published.UserDisplayName.Should().BeEmpty();
        published.Bits.Should().Be(50);
        published.Message.Should().Be("cheer50");
    }
}

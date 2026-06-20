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
/// Behaviour tests for the channel-points + Power-up fan-out translators. Each proves the consequence of
/// translating a real EventSub payload: a single typed domain event is published carrying the parsed fields,
/// the resolved tenant, and the injected (deterministic) timestamp — including the nested <c>reward</c> /
/// <c>custom_power_up</c> mapping.
/// </summary>
public sealed class ChannelPointsTranslatorsTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)
    );

    private static EventSubNotification Notification(
        Guid tenant,
        string type,
        string version,
        string payload
    )
    {
        using JsonDocument doc = JsonDocument.Parse(payload);
        return new EventSubNotification
        {
            MessageId = "msg-1",
            MessageTimestamp = new DateTimeOffset(2026, 6, 20, 11, 30, 0, TimeSpan.Zero),
            SubscriptionType = type,
            SubscriptionVersion = version,
            BroadcasterId = tenant,
            TwitchBroadcasterUserId = "1337",
            Event = doc.RootElement.Clone(),
        };
    }

    [Fact]
    public async Task RedemptionAdd_PublishesRewardRedeemedEvent_WithNestedRewardMapped()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelPointsRewardRedemptionAddTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.channel_points_custom_reward_redemption.add",
                "1",
                """
                {
                    "id": "17fa2df1-ad76-4804-bfa5-a40ef63efe63",
                    "broadcaster_user_id": "1337",
                    "user_id": "9001",
                    "user_login": "cooler_user",
                    "user_name": "Cooler_User",
                    "user_input": "pogchamp",
                    "status": "unfulfilled",
                    "reward": {
                        "id": "92af127c-7326-4483-a52b-b0da0be61c01",
                        "title": "title",
                        "cost": 100,
                        "prompt": "reward prompt"
                    },
                    "redeemed_at": "2020-07-15T17:16:03.17106713Z"
                }
                """
            )
        );

        RewardRedeemedEvent published = bus.EventsOf<RewardRedeemedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.RedemptionId.Should().Be("17fa2df1-ad76-4804-bfa5-a40ef63efe63");
        published.UserId.Should().Be("9001");
        published.UserDisplayName.Should().Be("Cooler_User");
        published.UserInput.Should().Be("pogchamp");
        published.RewardId.Should().Be("92af127c-7326-4483-a52b-b0da0be61c01");
        published.RewardTitle.Should().Be("title");
        published.Cost.Should().Be(100);
        published.Timestamp.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task RedemptionAdd_EmptyUserInput_DegradesToNull()
    {
        CapturingEventBus bus = new();
        ChannelPointsRewardRedemptionAddTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                Guid.NewGuid(),
                "channel.channel_points_custom_reward_redemption.add",
                "1",
                """
                {
                    "id": "r1",
                    "user_id": "9001",
                    "user_name": "Cooler_User",
                    "user_input": "",
                    "status": "unfulfilled",
                    "reward": { "id": "rw1", "title": "t", "cost": 5 }
                }
                """
            )
        );

        RewardRedeemedEvent published = bus.EventsOf<RewardRedeemedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.UserInput.Should().BeNull("an empty user_input carries no prompt text");
    }

    [Fact]
    public async Task RedemptionUpdate_PublishesRedemptionUpdatedEvent_WithFulfilledStatus()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelPointsRewardRedemptionUpdateTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.channel_points_custom_reward_redemption.update",
                "1",
                """
                {
                    "id": "17fa2df1-ad76-4804-bfa5-a40ef63efe63",
                    "broadcaster_user_id": "1337",
                    "user_id": "9001",
                    "user_login": "cooler_user",
                    "user_name": "Cooler_User",
                    "user_input": "pogchamp",
                    "status": "fulfilled",
                    "reward": {
                        "id": "92af127c-7326-4483-a52b-b0da0be61c01",
                        "title": "title",
                        "cost": 100,
                        "prompt": "reward prompt"
                    },
                    "redeemed_at": "2020-07-15T17:16:03.17106713Z"
                }
                """
            )
        );

        RewardRedemptionUpdatedEvent published = bus.EventsOf<RewardRedemptionUpdatedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.RedemptionId.Should().Be("17fa2df1-ad76-4804-bfa5-a40ef63efe63");
        published.UserId.Should().Be("9001");
        published.UserDisplayName.Should().Be("Cooler_User");
        published.Status.Should().Be("fulfilled");
        published.RewardId.Should().Be("92af127c-7326-4483-a52b-b0da0be61c01");
        published.RewardTitle.Should().Be("title");
        published.Timestamp.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task RewardAdd_PublishesRewardCreatedEvent_WithTopLevelFields()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelPointsRewardAddTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.channel_points_custom_reward.add",
                "1",
                """
                {
                    "id": "9001",
                    "broadcaster_user_id": "1337",
                    "broadcaster_user_login": "cool_user",
                    "broadcaster_user_name": "Cool_User",
                    "is_enabled": true,
                    "is_paused": false,
                    "is_in_stock": true,
                    "title": "Cool Reward",
                    "cost": 100,
                    "prompt": "reward prompt"
                }
                """
            )
        );

        RewardCreatedEvent published = bus.EventsOf<RewardCreatedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.TwitchRewardId.Should().Be("9001");
        published.Title.Should().Be("Cool Reward");
        published.Cost.Should().Be(100);
        published.IsEnabled.Should().BeTrue();
        published.Timestamp.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task RewardUpdate_PublishesRewardUpdatedEvent_WithDisabledFlag()
    {
        CapturingEventBus bus = new();
        ChannelPointsRewardUpdateTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                Guid.NewGuid(),
                "channel.channel_points_custom_reward.update",
                "1",
                """
                {
                    "id": "9001",
                    "is_enabled": false,
                    "title": "Renamed Reward",
                    "cost": 250,
                    "prompt": "p"
                }
                """
            )
        );

        RewardUpdatedEvent published = bus.EventsOf<RewardUpdatedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.TwitchRewardId.Should().Be("9001");
        published.Title.Should().Be("Renamed Reward");
        published.Cost.Should().Be(250);
        published.IsEnabled.Should().BeFalse("the updated reward was disabled");
    }

    [Fact]
    public async Task RewardRemove_PublishesRewardRemovedEvent()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelPointsRewardRemoveTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.channel_points_custom_reward.remove",
                "1",
                """
                {
                    "id": "9001",
                    "broadcaster_user_id": "1337",
                    "is_enabled": true,
                    "title": "Cool Reward",
                    "cost": 100
                }
                """
            )
        );

        RewardRemovedEvent published = bus.EventsOf<RewardRemovedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.TwitchRewardId.Should().Be("9001");
        published.Title.Should().Be("Cool Reward");
    }

    [Fact]
    public async Task AutomaticRedemptionAddV2_PublishesEvent_WithNestedRewardAndMessage()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelPointsAutomaticRewardRedemptionAddTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.channel_points_automatic_reward_redemption.add",
                "2",
                """
                {
                    "broadcaster_user_id": "12826",
                    "broadcaster_user_name": "Twitch",
                    "broadcaster_user_login": "twitch",
                    "user_id": "141981764",
                    "user_name": "TwitchDev",
                    "user_login": "twitchdev",
                    "id": "f024099a-e0fe-4339-9a0a-a706fb59f353",
                    "reward": {
                        "type": "send_highlighted_message",
                        "channel_points": 100,
                        "emote": null
                    },
                    "message": {
                        "text": "Hello world! VoHiYo",
                        "fragments": [
                            { "type": "text", "text": "Hello world! ", "emote": null }
                        ]
                    },
                    "redeemed_at": "2024-08-12T21:14:34.260398045Z"
                }
                """
            )
        );

        AutomaticRewardRedeemedEvent published = bus.EventsOf<AutomaticRewardRedeemedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.RedemptionId.Should().Be("f024099a-e0fe-4339-9a0a-a706fb59f353");
        published.UserId.Should().Be("141981764");
        published.UserLogin.Should().Be("twitchdev");
        published.UserDisplayName.Should().Be("TwitchDev");
        published.RewardType.Should().Be("send_highlighted_message");
        published.Cost.Should().Be(100);
        published.UnlockedEmoteId.Should().BeNull("the reward.emote was null for this reward type");
        published.Message.Should().Be("Hello world! VoHiYo");
        published.Timestamp.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task AutomaticRedemptionAddV2_UnlockEmote_MapsEmoteId()
    {
        CapturingEventBus bus = new();
        ChannelPointsAutomaticRewardRedemptionAddTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                Guid.NewGuid(),
                "channel.channel_points_automatic_reward_redemption.add",
                "2",
                """
                {
                    "broadcaster_user_id": "12826",
                    "user_id": "1",
                    "user_login": "u",
                    "user_name": "U",
                    "id": "red-1",
                    "reward": {
                        "type": "chosen_sub_emote_unlock",
                        "channel_points": 2000,
                        "emote": { "id": "81274", "set_id": "42" }
                    }
                }
                """
            )
        );

        AutomaticRewardRedeemedEvent published = bus.EventsOf<AutomaticRewardRedeemedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.RewardType.Should().Be("chosen_sub_emote_unlock");
        published.Cost.Should().Be(2000);
        published
            .UnlockedEmoteId.Should()
            .Be("81274", "the unlocked emote id is mapped off reward.emote");
        published.Message.Should().BeNull("no message accompanies an emote-unlock redemption");
    }

    [Fact]
    public async Task CustomPowerUpRedemptionAdd_PublishesEvent_WithNestedPowerUpMapped()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelCustomPowerUpRedemptionAddTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.custom_power_up_redemption.add",
                "1",
                """
                {
                    "id": "17fa2df1-ad76-4804-bfa5-a40ef63efe63",
                    "broadcaster_user_id": "1337",
                    "broadcaster_user_login": "cool_user",
                    "broadcaster_user_name": "Cool_User",
                    "user_id": "9001",
                    "user_login": "cooler_user",
                    "user_name": "Cooler_User",
                    "user_input": "pogchamp",
                    "status": "unfulfilled",
                    "custom_power_up": {
                        "id": "92af127c-7326-4483-a52b-b0da0be61c01",
                        "title": "title",
                        "bits": 100,
                        "prompt": "Power-up prompt"
                    },
                    "redeemed_at": "2026-05-01T17:16:03.17106713Z"
                }
                """
            )
        );

        CustomPowerUpRedeemedEvent published = bus.EventsOf<CustomPowerUpRedeemedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.RedemptionId.Should().Be("17fa2df1-ad76-4804-bfa5-a40ef63efe63");
        published.UserId.Should().Be("9001");
        published.UserLogin.Should().Be("cooler_user");
        published.UserDisplayName.Should().Be("Cooler_User");
        published.Status.Should().Be("unfulfilled");
        published.UserInput.Should().Be("pogchamp");
        published.PowerUpId.Should().Be("92af127c-7326-4483-a52b-b0da0be61c01");
        published.PowerUpTitle.Should().Be("title");
        published.Bits.Should().Be(100);
        published.Timestamp.Should().Be(Clock.GetUtcNow());
    }
}

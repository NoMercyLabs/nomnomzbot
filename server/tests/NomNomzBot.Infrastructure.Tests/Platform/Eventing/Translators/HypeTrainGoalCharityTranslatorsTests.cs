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
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Infrastructure.Platform.Eventing.Translators;
using NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing.Translators;

/// <summary>
/// Behaviour tests for the hype-train (v2), goal, and charity translators. Each runs a realistic raw payload
/// through the translator with a <see cref="CapturingEventBus"/> + deterministic clock and asserts the published
/// event's concrete type and parsed field values — including nested hype-train contributions, goal achievement,
/// and the charity money amount mapped as raw minor units / decimal places / currency (never pre-divided).
/// </summary>
public sealed class HypeTrainGoalCharityTranslatorsTests
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
            SubscriptionVersion = type.StartsWith("channel.hype_train") ? "2" : "1",
            BroadcasterId = tenant,
            TwitchBroadcasterUserId = "broadcaster-99",
            Event = doc.RootElement.Clone(),
        };
    }

    [Fact]
    public async Task HypeTrainBegin_PublishesBeganEvent_WithContributionsAndGoal()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelHypeTrainBeginTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.hype_train.begin",
                """
                {
                    "id": "ht-1",
                    "broadcaster_user_id": "1337",
                    "level": 2,
                    "total": 700,
                    "progress": 200,
                    "goal": 1000,
                    "top_contributions": [
                        { "user_id": "u1", "user_login": "alice", "user_name": "Alice", "type": "bits", "total": 500 },
                        { "user_id": "u2", "user_login": "bob", "user_name": "Bob", "type": "subscription", "total": 200 }
                    ],
                    "started_at": "2026-06-20T11:30:00Z",
                    "expires_at": "2026-06-20T11:35:00Z"
                }
                """
            )
        );

        HypeTrainBeganEvent published = bus.EventsOf<HypeTrainBeganEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.HypeTrainId.Should().Be("ht-1");
        published.Level.Should().Be(2);
        published.Total.Should().Be(700);
        published.Progress.Should().Be(200);
        published.Goal.Should().Be(1000);
        published.ExpiresAt.Should().Be(new DateTimeOffset(2026, 6, 20, 11, 35, 0, TimeSpan.Zero));
        published.TopContributions.Should().HaveCount(2);
        published
            .TopContributions[0]
            .Should()
            .Be(new HypeTrainContribution("u1", "alice", "Alice", "bits", 500));
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task HypeTrainProgress_PublishesProgressEvent_WithAdvancingTotals()
    {
        CapturingEventBus bus = new();
        ChannelHypeTrainProgressTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                Guid.NewGuid(),
                "channel.hype_train.progress",
                """
                {
                    "id": "ht-1",
                    "level": 3,
                    "total": 1200,
                    "progress": 200,
                    "goal": 1500,
                    "top_contributions": [
                        { "user_id": "u1", "user_login": "alice", "user_name": "Alice", "type": "bits", "total": 900 }
                    ],
                    "expires_at": "2026-06-20T11:36:00Z"
                }
                """
            )
        );

        HypeTrainProgressEvent published = bus.EventsOf<HypeTrainProgressEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.Level.Should().Be(3);
        published.Total.Should().Be(1200);
        published.Goal.Should().Be(1500);
        published.TopContributions.Should().ContainSingle();
    }

    [Fact]
    public async Task HypeTrainEnd_PublishesEndedEvent_WithFinalLevelAndContributions()
    {
        CapturingEventBus bus = new();
        ChannelHypeTrainEndTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                Guid.NewGuid(),
                "channel.hype_train.end",
                """
                {
                    "id": "ht-1",
                    "level": 5,
                    "total": 3500,
                    "top_contributions": [
                        { "user_id": "u1", "user_login": "alice", "user_name": "Alice", "type": "bits", "total": 2000 }
                    ],
                    "started_at": "2026-06-20T11:30:00Z",
                    "ended_at": "2026-06-20T11:40:00Z"
                }
                """
            )
        );

        HypeTrainEndedEvent published = bus.EventsOf<HypeTrainEndedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.Level.Should().Be(5);
        published.Total.Should().Be(3500);
        published.EndedAt.Should().Be(new DateTimeOffset(2026, 6, 20, 11, 40, 0, TimeSpan.Zero));
        published.TopContributions[0].Total.Should().Be(2000);
    }

    [Fact]
    public async Task GoalBegin_PublishesGoalBeganEvent_WithTypeAndAmounts()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelGoalBeginTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.goal.begin",
                """
                {
                    "id": "goal-1",
                    "broadcaster_user_id": "1337",
                    "type": "follower",
                    "description": "Road to 1k followers",
                    "current_amount": 850,
                    "target_amount": 1000,
                    "started_at": "2026-06-20T11:00:00Z"
                }
                """
            )
        );

        GoalBeganEvent published = bus.EventsOf<GoalBeganEvent>().Should().ContainSingle().Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.GoalId.Should().Be("goal-1");
        published.Type.Should().Be("follower");
        published.Description.Should().Be("Road to 1k followers");
        published.CurrentAmount.Should().Be(850);
        published.TargetAmount.Should().Be(1000);
        published.StartedAt.Should().Be(new DateTimeOffset(2026, 6, 20, 11, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task GoalProgress_PublishesGoalProgressEvent_WithUpdatedAmount()
    {
        CapturingEventBus bus = new();
        ChannelGoalProgressTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                Guid.NewGuid(),
                "channel.goal.progress",
                """
                {
                    "id": "goal-1",
                    "type": "subscription",
                    "description": "Sub goal",
                    "current_amount": 920,
                    "target_amount": 1000,
                    "started_at": "2026-06-20T11:00:00Z"
                }
                """
            )
        );

        GoalProgressEvent published = bus.EventsOf<GoalProgressEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.Type.Should().Be("subscription");
        published.CurrentAmount.Should().Be(920);
        published.TargetAmount.Should().Be(1000);
    }

    [Fact]
    public async Task GoalEnd_PublishesGoalEndedEvent_WithAchievementAndEndTime()
    {
        CapturingEventBus bus = new();
        ChannelGoalEndTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                Guid.NewGuid(),
                "channel.goal.end",
                """
                {
                    "id": "goal-1",
                    "type": "follower",
                    "description": "Road to 1k followers",
                    "is_achieved": true,
                    "current_amount": 1000,
                    "target_amount": 1000,
                    "started_at": "2026-06-20T11:00:00Z",
                    "ended_at": "2026-06-20T11:50:00Z"
                }
                """
            )
        );

        GoalEndedEvent published = bus.EventsOf<GoalEndedEvent>().Should().ContainSingle().Subject;
        published.IsAchieved.Should().BeTrue("current_amount reached target_amount");
        published.CurrentAmount.Should().Be(1000);
        published.EndedAt.Should().Be(new DateTimeOffset(2026, 6, 20, 11, 50, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task CharityStart_PublishesStartedEvent_WithRawMoneyAmounts()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelCharityCampaignStartTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.charity_campaign.start",
                """
                {
                    "id": "camp-1",
                    "broadcaster_user_id": "1337",
                    "charity_name": "Save the Cats",
                    "charity_description": "Helping cats everywhere",
                    "charity_logo": "https://abc/logo.png",
                    "charity_website": "https://savethecats.example",
                    "current_amount": { "value": 150000, "decimal_places": 2, "currency": "USD" },
                    "target_amount": { "value": 1500000, "decimal_places": 2, "currency": "USD" },
                    "started_at": "2026-06-20T11:00:00Z"
                }
                """
            )
        );

        CharityCampaignStartedEvent published = bus.EventsOf<CharityCampaignStartedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.CampaignId.Should().Be("camp-1");
        published.CharityName.Should().Be("Save the Cats");
        published.Description.Should().Be("Helping cats everywhere");
        published
            .CurrentAmountValue.Should()
            .Be(150000, "the raw minor-unit value is carried, never pre-divided");
        published.CurrentAmountDecimalPlaces.Should().Be(2);
        published.CurrentAmountCurrency.Should().Be("USD");
        published.TargetAmountValue.Should().Be(1500000);
        published.TargetAmountDecimalPlaces.Should().Be(2);
        published.TargetAmountCurrency.Should().Be("USD");
        published.StartedAt.Should().Be(new DateTimeOffset(2026, 6, 20, 11, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task CharityProgress_PublishesProgressEvent_WithUpdatedCurrentAmount()
    {
        CapturingEventBus bus = new();
        ChannelCharityCampaignProgressTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                Guid.NewGuid(),
                "channel.charity_campaign.progress",
                """
                {
                    "id": "camp-1",
                    "charity_name": "Save the Cats",
                    "current_amount": { "value": 260000, "decimal_places": 2, "currency": "USD" },
                    "target_amount": { "value": 1500000, "decimal_places": 2, "currency": "USD" }
                }
                """
            )
        );

        CharityCampaignProgressEvent published = bus.EventsOf<CharityCampaignProgressEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.CurrentAmountValue.Should().Be(260000);
        published.TargetAmountValue.Should().Be(1500000);
        published.CurrentAmountCurrency.Should().Be("USD");
    }

    [Fact]
    public async Task CharityDonate_PublishesDonationEvent_WithDonorAndRawAmount()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelCharityCampaignDonateTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.charity_campaign.donate",
                """
                {
                    "id": "donation-9",
                    "campaign_id": "camp-1",
                    "broadcaster_user_id": "1337",
                    "user_id": "u7",
                    "user_login": "generous_gary",
                    "user_name": "Generous_Gary",
                    "charity_name": "Save the Cats",
                    "amount": { "value": 5000, "decimal_places": 2, "currency": "EUR" }
                }
                """
            )
        );

        CharityDonationEvent published = bus.EventsOf<CharityDonationEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.CampaignId.Should().Be("camp-1");
        published.CharityName.Should().Be("Save the Cats");
        published.UserId.Should().Be("u7");
        published.UserLogin.Should().Be("generous_gary");
        published.UserDisplayName.Should().Be("Generous_Gary");
        published
            .AmountValue.Should()
            .Be(5000, "50.00 EUR is carried as 5000 minor units, never pre-divided");
        published.AmountDecimalPlaces.Should().Be(2);
        published.AmountCurrency.Should().Be("EUR");
    }

    [Fact]
    public async Task CharityStop_PublishesStoppedEvent_WithFinalAmountAndStopTime()
    {
        CapturingEventBus bus = new();
        ChannelCharityCampaignStopTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                Guid.NewGuid(),
                "channel.charity_campaign.stop",
                """
                {
                    "id": "camp-1",
                    "charity_name": "Save the Cats",
                    "current_amount": { "value": 1500000, "decimal_places": 2, "currency": "USD" },
                    "target_amount": { "value": 1500000, "decimal_places": 2, "currency": "USD" },
                    "stopped_at": "2026-06-20T12:00:00Z"
                }
                """
            )
        );

        CharityCampaignStoppedEvent published = bus.EventsOf<CharityCampaignStoppedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.CurrentAmountValue.Should().Be(1500000);
        published.StoppedAt.Should().Be(new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero));
    }
}

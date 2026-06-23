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
/// Behaviour tests for the poll and prediction translators. Each feeds a realistic raw EventSub payload through
/// the translator with a <see cref="CapturingEventBus"/> + deterministic clock and asserts the published event's
/// concrete type and parsed field values — including the nested choice/outcome vote tallies and the terminal
/// status + winner on the end events.
/// </summary>
public sealed class PollPredictionTranslatorsTests
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
    public async Task PollBegin_PublishesPollBeganEvent_WithChoicesAndDuration()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelPollBeginTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.poll.begin",
                """
                {
                    "id": "poll-1",
                    "broadcaster_user_id": "1337",
                    "title": "Pineapple on pizza?",
                    "choices": [
                        { "id": "c1", "title": "Yes", "bits_votes": 0, "channel_points_votes": 10, "votes": 10 },
                        { "id": "c2", "title": "No", "bits_votes": 0, "channel_points_votes": 0, "votes": 0 }
                    ],
                    "started_at": "2026-06-20T11:30:00Z",
                    "ends_at": "2026-06-20T11:32:00Z"
                }
                """
            )
        );

        PollBeganEvent published = bus.EventsOf<PollBeganEvent>().Should().ContainSingle().Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.PollId.Should().Be("poll-1");
        published.Title.Should().Be("Pineapple on pizza?");
        published.DurationSeconds.Should().Be(120);
        published.EndsAt.Should().Be(new DateTimeOffset(2026, 6, 20, 11, 32, 0, TimeSpan.Zero));
        published.Choices.Should().HaveCount(2);
        published.Choices[0].Should().Be(new PollChoice("c1", "Yes", 10, 10));
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task PollProgress_PublishesPollProgressEvent_WithRunningTallies()
    {
        CapturingEventBus bus = new();
        ChannelPollProgressTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                Guid.NewGuid(),
                "channel.poll.progress",
                """
                {
                    "id": "poll-1",
                    "title": "Pineapple on pizza?",
                    "choices": [
                        { "id": "c1", "title": "Yes", "channel_points_votes": 25, "votes": 30 },
                        { "id": "c2", "title": "No", "channel_points_votes": 5, "votes": 12 }
                    ],
                    "ends_at": "2026-06-20T11:32:00Z"
                }
                """
            )
        );

        PollProgressEvent published = bus.EventsOf<PollProgressEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.PollId.Should().Be("poll-1");
        published.Choices.Should().HaveCount(2);
        published.Choices[0].Votes.Should().Be(30);
        published.Choices[0].ChannelPointsVotes.Should().Be(25);
        published.Choices[1].Votes.Should().Be(12);
        published.EndsAt.Should().Be(new DateTimeOffset(2026, 6, 20, 11, 32, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task PollEnd_PublishesPollEndedEvent_WithStatusAndWinningChoice()
    {
        CapturingEventBus bus = new();
        ChannelPollEndTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                Guid.NewGuid(),
                "channel.poll.end",
                """
                {
                    "id": "poll-1",
                    "title": "Pineapple on pizza?",
                    "status": "completed",
                    "choices": [
                        { "id": "c1", "title": "Yes", "channel_points_votes": 25, "votes": 30 },
                        { "id": "c2", "title": "No", "channel_points_votes": 5, "votes": 42 }
                    ]
                }
                """
            )
        );

        PollEndedEvent published = bus.EventsOf<PollEndedEvent>().Should().ContainSingle().Subject;
        published.Status.Should().Be("completed");
        published.WinningChoiceId.Should().Be("c2", "c2 received the most total votes (42 vs 30)");
        published.Choices.Should().HaveCount(2);
    }

    [Fact]
    public async Task PollEnd_NoVotes_HasNoWinningChoice()
    {
        CapturingEventBus bus = new();
        ChannelPollEndTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                Guid.NewGuid(),
                "channel.poll.end",
                """
                {
                    "id": "poll-2",
                    "title": "Nobody voted",
                    "status": "terminated",
                    "choices": [
                        { "id": "c1", "title": "A", "votes": 0 },
                        { "id": "c2", "title": "B", "votes": 0 }
                    ]
                }
                """
            )
        );

        PollEndedEvent published = bus.EventsOf<PollEndedEvent>().Should().ContainSingle().Subject;
        published.Status.Should().Be("terminated");
        published.WinningChoiceId.Should().BeNull("no choice received any votes");
    }

    [Fact]
    public async Task PredictionBegin_PublishesPredictionBeganEvent_WithOutcomesAndWindow()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelPredictionBeginTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.prediction.begin",
                """
                {
                    "id": "pred-1",
                    "title": "Will we win?",
                    "outcomes": [
                        { "id": "o1", "title": "Yes", "color": "blue", "users": 0, "channel_points": 0 },
                        { "id": "o2", "title": "No", "color": "pink", "users": 0, "channel_points": 0 }
                    ],
                    "started_at": "2026-06-20T11:30:00Z",
                    "locks_at": "2026-06-20T11:31:30Z"
                }
                """
            )
        );

        PredictionBeganEvent published = bus.EventsOf<PredictionBeganEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.PredictionId.Should().Be("pred-1");
        published.WindowSeconds.Should().Be(90);
        published.LocksAt.Should().Be(new DateTimeOffset(2026, 6, 20, 11, 31, 30, TimeSpan.Zero));
        published.Outcomes.Should().HaveCount(2);
        published.Outcomes[0].Should().Be(new PredictionOutcome("o1", "Yes", 0, 0, "blue"));
    }

    [Fact]
    public async Task PredictionProgress_PublishesPredictionProgressEvent_WithPools()
    {
        CapturingEventBus bus = new();
        ChannelPredictionProgressTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                Guid.NewGuid(),
                "channel.prediction.progress",
                """
                {
                    "id": "pred-1",
                    "title": "Will we win?",
                    "outcomes": [
                        { "id": "o1", "title": "Yes", "color": "blue", "users": 12, "channel_points": 5000 },
                        { "id": "o2", "title": "No", "color": "pink", "users": 3, "channel_points": 800 }
                    ],
                    "locks_at": "2026-06-20T11:31:30Z"
                }
                """
            )
        );

        PredictionProgressEvent published = bus.EventsOf<PredictionProgressEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.Outcomes.Should().HaveCount(2);
        published.Outcomes[0].Users.Should().Be(12);
        published.Outcomes[0].ChannelPoints.Should().Be(5000);
        published.LocksAt.Should().Be(new DateTimeOffset(2026, 6, 20, 11, 31, 30, TimeSpan.Zero));
    }

    [Fact]
    public async Task PredictionLock_PublishesPredictionLockedEvent_WithOutcomes()
    {
        CapturingEventBus bus = new();
        ChannelPredictionLockTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                Guid.NewGuid(),
                "channel.prediction.lock",
                """
                {
                    "id": "pred-1",
                    "title": "Will we win?",
                    "outcomes": [
                        { "id": "o1", "title": "Yes", "color": "blue", "users": 12, "channel_points": 5000 }
                    ]
                }
                """
            )
        );

        PredictionLockedEvent published = bus.EventsOf<PredictionLockedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.PredictionId.Should().Be("pred-1");
        published.Outcomes.Should().ContainSingle();
    }

    [Fact]
    public async Task PredictionEnd_PublishesPredictionEndedEvent_WithStatusAndWinner()
    {
        CapturingEventBus bus = new();
        ChannelPredictionEndTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                Guid.NewGuid(),
                "channel.prediction.end",
                """
                {
                    "id": "pred-1",
                    "title": "Will we win?",
                    "winning_outcome_id": "o1",
                    "status": "resolved",
                    "outcomes": [
                        { "id": "o1", "title": "Yes", "color": "blue", "users": 12, "channel_points": 5000 },
                        { "id": "o2", "title": "No", "color": "pink", "users": 3, "channel_points": 800 }
                    ],
                    "started_at": "2026-06-20T11:30:00Z",
                    "ended_at": "2026-06-20T11:35:00Z"
                }
                """
            )
        );

        PredictionEndedEvent published = bus.EventsOf<PredictionEndedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.Status.Should().Be("resolved");
        published.WinningOutcomeId.Should().Be("o1");
        published.Outcomes.Should().HaveCount(2);
    }

    [Fact]
    public async Task PredictionEnd_Canceled_HasNullWinningOutcome()
    {
        CapturingEventBus bus = new();
        ChannelPredictionEndTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                Guid.NewGuid(),
                "channel.prediction.end",
                """
                {
                    "id": "pred-2",
                    "title": "Canceled one",
                    "status": "canceled",
                    "outcomes": [
                        { "id": "o1", "title": "Yes", "color": "blue", "users": 0, "channel_points": 0 }
                    ]
                }
                """
            )
        );

        PredictionEndedEvent published = bus.EventsOf<PredictionEndedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.Status.Should().Be("canceled");
        published.WinningOutcomeId.Should().BeNull("a canceled prediction has no winning outcome");
    }
}

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
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Infrastructure.Platform.Eventing.Translators;
using NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing.Translators;

/// <summary>
/// Behaviour tests for the stream-lifecycle fan-out translators (raid, channel.update, stream.online,
/// stream.offline). Each runs a realistic raw EventSub <c>event</c> payload through its translator with a
/// capturing bus and a deterministic clock, then asserts the published event's concrete type and parsed
/// field values — including the started_at parse and the degraded fields Twitch does not carry on these
/// notifications.
/// </summary>
public sealed class StreamLifecycleTranslatorsTests
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
    public async Task ChannelRaid_PublishesRaidEvent_FromIncomingRaiderFields()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelRaidTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.raid",
                """
                {
                    "from_broadcaster_user_id": "5678",
                    "from_broadcaster_user_login": "raiding_streamer",
                    "from_broadcaster_user_name": "Raiding_Streamer",
                    "to_broadcaster_user_id": "broadcaster-99",
                    "to_broadcaster_user_login": "streamer",
                    "to_broadcaster_user_name": "Streamer",
                    "viewers": 250
                }
                """
            )
        );

        RaidEvent published = bus.EventsOf<RaidEvent>().Should().ContainSingle().Subject;
        published.BroadcasterId.Should().Be(tenant, "the dispatcher resolved the raided tenant");
        published.FromUserId.Should().Be("5678", "the event carries the raiding (from) party");
        published.FromDisplayName.Should().Be("Raiding_Streamer");
        published.FromLogin.Should().Be("raiding_streamer");
        published.ViewerCount.Should().Be(250);
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task ChannelUpdate_PublishesChannelUpdatedEvent_WithTitleAndCategory()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelUpdateTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.update",
                """
                {
                    "broadcaster_user_id": "broadcaster-99",
                    "broadcaster_user_login": "streamer",
                    "broadcaster_user_name": "Streamer",
                    "title": "New title!",
                    "language": "en",
                    "category_id": "509658",
                    "category_name": "Just Chatting",
                    "content_classification_labels": []
                }
                """
            )
        );

        ChannelUpdatedEvent published = bus.EventsOf<ChannelUpdatedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.BroadcasterDisplayName.Should().Be("Streamer");
        published.NewTitle.Should().Be("New title!");
        published.NewGameName.Should().Be("Just Chatting");
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task StreamOnline_PublishesChannelOnlineEvent_WithParsedStartedAt()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        StreamOnlineTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "stream.online",
                """
                {
                    "id": "9001",
                    "broadcaster_user_id": "broadcaster-99",
                    "broadcaster_user_login": "streamer",
                    "broadcaster_user_name": "Streamer",
                    "type": "live",
                    "started_at": "2026-06-20T11:25:00Z"
                }
                """
            )
        );

        ChannelOnlineEvent published = bus.EventsOf<ChannelOnlineEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.BroadcasterDisplayName.Should().Be("Streamer");
        published
            .StartedAt.Should()
            .Be(
                new DateTimeOffset(2026, 6, 20, 11, 25, 0, TimeSpan.Zero),
                "started_at is parsed from the payload"
            );
        published
            .StreamTitle.Should()
            .BeEmpty("stream.online carries no title — it hydrates from Helix/channel.update");
        published.GameName.Should().BeEmpty();
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task StreamOnline_MissingStartedAt_FallsBackToClock()
    {
        CapturingEventBus bus = new();
        StreamOnlineTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                Guid.NewGuid(),
                "stream.online",
                """{ "broadcaster_user_name": "Streamer", "type": "live" }"""
            )
        );

        ChannelOnlineEvent published = bus.EventsOf<ChannelOnlineEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published
            .StartedAt.Should()
            .Be(
                Clock.GetUtcNow(),
                "an absent started_at degrades to the publish clock, never throws"
            );
    }

    [Fact]
    public async Task StreamOffline_PublishesChannelOfflineEvent_WithBroadcasterAndZeroDuration()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        StreamOfflineTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "stream.offline",
                """
                {
                    "broadcaster_user_id": "broadcaster-99",
                    "broadcaster_user_login": "streamer",
                    "broadcaster_user_name": "Streamer"
                }
                """
            )
        );

        ChannelOfflineEvent published = bus.EventsOf<ChannelOfflineEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.BroadcasterDisplayName.Should().Be("Streamer");
        published
            .StreamDuration.Should()
            .Be(
                TimeSpan.Zero,
                "stream.offline carries no duration — uptime is computed downstream"
            );
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }
}

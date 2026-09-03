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
    private static readonly FakeTimeProvider Clock = new(new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero));

    private static EventSubNotification Notification(Guid tenant, string type, string payload)
    {
        using JsonDocument doc = JsonDocument.Parse(payload);
        return new()
        {
            MessageId = "msg-1",
            MessageTimestamp = new(2026, 6, 20, 11, 30, 0, TimeSpan.Zero),
            SubscriptionType = type,
            SubscriptionVersion = "1",
            BroadcasterId = tenant,
            TwitchBroadcasterUserId = "broadcaster-99",
            Event = doc.RootElement.Clone(),
        };
    }

    // One Twitch topic, two directions. We subscribe channel.raid twice — keyed on to_broadcaster_user_id
    // for raids arriving here, and on from_broadcaster_user_id for raids this channel SENT. The from_-keyed
    // one is the only report Twitch gives that a raid actually executed and the viewers moved; before it
    // existed the sole outgoing signal was channel.moderate's `raid` action, which fires when the countdown
    // STARTS — so the raid-committed pipeline ended the broadcast at the beginning of the raid.

    [Fact]
    public async Task ChannelRaid_WhenThisChannelIsTheRaider_PublishesTheExecutedOutgoingRaid()
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
                    "from_broadcaster_user_id": "broadcaster-99",
                    "from_broadcaster_user_login": "streamer",
                    "from_broadcaster_user_name": "Streamer",
                    "to_broadcaster_user_id": "5678",
                    "to_broadcaster_user_login": "friendly_streamer",
                    "to_broadcaster_user_name": "Friendly_Streamer",
                    "viewers": 250
                }
                """
            )
        );

        OutgoingRaidEvent published = bus.EventsOf<OutgoingRaidEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.ToUserId.Should().Be("5678", "the outgoing event names the channel we raided");
        published.ToDisplayName.Should().Be("Friendly_Streamer");
        published.ToLogin.Should().Be("friendly_streamer");
        published.ViewerCount.Should().Be(250);

        bus.EventsOf<RaidEvent>()
            .Should()
            .BeEmpty(
                "a raid we sent is not a raid we received — the direction decides, and only one fires"
            );
    }

    [Fact]
    public async Task ChannelRaid_DirectionIsDecidedByWhichSideIsUs_NotByFieldOrder()
    {
        // The payload names both sides on every raid notification, so a translator that simply read
        // from_/to_ would publish the wrong direction for one of the two subscriptions. The resolved
        // TwitchBroadcasterUserId — which the transport takes from the subscription's own condition — is
        // what settles it.
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelRaidTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.raid",
                """
                {
                    "from_broadcaster_user_id": "someone-else",
                    "from_broadcaster_user_login": "raider",
                    "from_broadcaster_user_name": "Raider",
                    "to_broadcaster_user_id": "broadcaster-99",
                    "to_broadcaster_user_login": "streamer",
                    "to_broadcaster_user_name": "Streamer",
                    "viewers": 12
                }
                """
            )
        );

        bus.EventsOf<RaidEvent>().Should().ContainSingle();
        bus.EventsOf<OutgoingRaidEvent>()
            .Should()
            .BeEmpty("we are the raided side here, so nothing outgoing may fire");
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
                new(2026, 6, 20, 11, 25, 0, TimeSpan.Zero),
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

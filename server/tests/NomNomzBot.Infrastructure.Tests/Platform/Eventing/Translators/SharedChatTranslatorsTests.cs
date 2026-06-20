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
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Infrastructure.Platform.Eventing.Translators;
using NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing.Translators;

/// <summary>
/// Behaviour tests for the shared-chat fan-out translators. Each runs a realistic raw EventSub <c>event</c>
/// payload through its translator with a capturing bus and a deterministic clock, then asserts the published
/// event's concrete type and parsed fields — including the session identity, the host fields, and the parsed
/// <c>participants</c> broadcaster-id list (and that it degrades to empty when the array is absent).
/// </summary>
public sealed class SharedChatTranslatorsTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)
    );

    private static EventSubNotification Notification(Guid tenant, string type, string payload)
    {
        using JsonDocument doc = JsonDocument.Parse(payload);
        return new EventSubNotification
        {
            MessageId = "msg-shared-chat-1",
            MessageTimestamp = new DateTimeOffset(2026, 6, 20, 11, 30, 0, TimeSpan.Zero),
            SubscriptionType = type,
            SubscriptionVersion = "1",
            BroadcasterId = tenant,
            TwitchBroadcasterUserId = "broadcaster-99",
            Event = doc.RootElement.Clone(),
        };
    }

    [Fact]
    public async Task SharedChatBegin_PublishesSharedChatBeganEvent_WithHostAndParticipants()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelSharedChatBeginTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.shared_chat.begin",
                """
                {
                    "session_id": "session-abc",
                    "broadcaster_user_id": "broadcaster-99",
                    "broadcaster_user_login": "streamer",
                    "broadcaster_user_name": "Streamer",
                    "host_broadcaster_user_id": "host-1",
                    "host_broadcaster_user_login": "host_streamer",
                    "host_broadcaster_user_name": "Host_Streamer",
                    "participants": [
                        { "broadcaster_user_id": "host-1", "broadcaster_user_login": "host_streamer", "broadcaster_user_name": "Host_Streamer" },
                        { "broadcaster_user_id": "guest-2", "broadcaster_user_login": "guest_streamer", "broadcaster_user_name": "Guest_Streamer" }
                    ]
                }
                """
            )
        );

        SharedChatBeganEvent published = bus.EventsOf<SharedChatBeganEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant, "the dispatcher resolved the tenant");
        published.SessionId.Should().Be("session-abc");
        published.HostBroadcasterId.Should().Be("host-1");
        published.HostBroadcasterDisplayName.Should().Be("Host_Streamer");
        published.HostBroadcasterLogin.Should().Be("host_streamer");
        published.Participants.Should().Equal("host-1", "guest-2");
        published.Timestamp.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task SharedChatUpdate_PublishesSharedChatUpdatedEvent_WithCurrentParticipants()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelSharedChatUpdateTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.shared_chat.update",
                """
                {
                    "session_id": "session-abc",
                    "host_broadcaster_user_id": "host-1",
                    "host_broadcaster_user_login": "host_streamer",
                    "host_broadcaster_user_name": "Host_Streamer",
                    "participants": [
                        { "broadcaster_user_id": "host-1" },
                        { "broadcaster_user_id": "guest-2" },
                        { "broadcaster_user_id": "guest-3" }
                    ]
                }
                """
            )
        );

        SharedChatUpdatedEvent published = bus.EventsOf<SharedChatUpdatedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.SessionId.Should().Be("session-abc");
        published.HostBroadcasterId.Should().Be("host-1");
        published.Participants.Should().Equal("host-1", "guest-2", "guest-3");
        published.Timestamp.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task SharedChatUpdate_MissingParticipants_DegradesToEmptyList()
    {
        CapturingEventBus bus = new();
        ChannelSharedChatUpdateTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                Guid.NewGuid(),
                "channel.shared_chat.update",
                """{ "session_id": "session-abc", "host_broadcaster_user_id": "host-1" }"""
            )
        );

        SharedChatUpdatedEvent published = bus.EventsOf<SharedChatUpdatedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published
            .Participants.Should()
            .BeEmpty("an absent participants array degrades to empty, never throws");
    }

    [Fact]
    public async Task SharedChatEnd_PublishesSharedChatEndedEvent_WithSessionAndHost()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelSharedChatEndTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.shared_chat.end",
                """
                {
                    "session_id": "session-abc",
                    "broadcaster_user_id": "broadcaster-99",
                    "host_broadcaster_user_id": "host-1",
                    "host_broadcaster_user_login": "host_streamer",
                    "host_broadcaster_user_name": "Host_Streamer"
                }
                """
            )
        );

        SharedChatEndedEvent published = bus.EventsOf<SharedChatEndedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.SessionId.Should().Be("session-abc");
        published.HostBroadcasterId.Should().Be("host-1");
        published.Timestamp.Should().Be(Clock.GetUtcNow());
    }
}

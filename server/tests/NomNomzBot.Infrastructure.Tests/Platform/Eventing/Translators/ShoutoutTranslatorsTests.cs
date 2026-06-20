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
/// Behaviour tests for the shoutout translators: an outgoing <c>channel.shoutout.create</c> maps onto the shared
/// <see cref="ShoutoutSentEvent"/> (recipient identity), and an incoming <c>channel.shoutout.receive</c> maps onto
/// <see cref="ShoutoutReceivedEvent"/> (source identity + exposed viewer count). Each asserts the published event
/// type, fields, resolved tenant, and injected clock.
/// </summary>
public sealed class ShoutoutTranslatorsTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)
    );

    private static readonly Guid Tenant = Guid.NewGuid();

    private static EventSubNotification Notification(string subscriptionType, string payload)
    {
        using JsonDocument doc = JsonDocument.Parse(payload);
        return new EventSubNotification
        {
            MessageId = "msg-1",
            MessageTimestamp = new DateTimeOffset(2026, 6, 20, 11, 30, 0, TimeSpan.Zero),
            SubscriptionType = subscriptionType,
            SubscriptionVersion = "1",
            BroadcasterId = Tenant,
            TwitchBroadcasterUserId = "broadcaster-99",
            Event = doc.RootElement.Clone(),
        };
    }

    [Fact]
    public async Task ShoutoutCreate_PublishesSentEvent_WithTargetBroadcaster()
    {
        CapturingEventBus bus = new();
        ChannelShoutoutCreateTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                "channel.shoutout.create",
                """
                {
                    "broadcaster_user_id": "broadcaster-99",
                    "to_broadcaster_user_id": "626",
                    "to_broadcaster_user_login": "friend_streamer",
                    "to_broadcaster_user_name": "Friend_Streamer",
                    "moderator_user_id": "98765",
                    "moderator_user_login": "mod",
                    "moderator_user_name": "Mod",
                    "viewer_count": 860,
                    "started_at": "2026-06-20T11:29:00Z",
                    "cooldown_ends_at": "2026-06-20T11:31:00Z",
                    "target_cooldown_ends_at": "2026-06-20T12:30:00Z"
                }
                """
            )
        );

        ShoutoutSentEvent published = bus.EventsOf<ShoutoutSentEvent>()
            .Should()
            .ContainSingle()
            .Subject;

        published.BroadcasterId.Should().Be(Tenant, "the dispatcher resolved the tenant");
        published.Timestamp.Should().Be(Clock.GetUtcNow());
        published.ToUserId.Should().Be("626");
        published.ToDisplayName.Should().Be("Friend_Streamer");
    }

    [Fact]
    public async Task ShoutoutReceive_PublishesReceivedEvent_WithSourceAndViewerCount()
    {
        CapturingEventBus bus = new();
        ChannelShoutoutReceiveTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                "channel.shoutout.receive",
                """
                {
                    "broadcaster_user_id": "broadcaster-99",
                    "from_broadcaster_user_id": "12345",
                    "from_broadcaster_user_login": "big_streamer",
                    "from_broadcaster_user_name": "Big_Streamer",
                    "viewer_count": 3500,
                    "started_at": "2026-06-20T11:29:00Z"
                }
                """
            )
        );

        ShoutoutReceivedEvent published = bus.EventsOf<ShoutoutReceivedEvent>()
            .Should()
            .ContainSingle()
            .Subject;

        published.BroadcasterId.Should().Be(Tenant, "the dispatcher resolved the tenant");
        published.Timestamp.Should().Be(Clock.GetUtcNow());
        published.FromBroadcasterId.Should().Be("12345");
        published.FromBroadcasterLogin.Should().Be("big_streamer");
        published.FromBroadcasterDisplayName.Should().Be("Big_Streamer");
        published.ViewerCount.Should().Be(3500);
    }
}

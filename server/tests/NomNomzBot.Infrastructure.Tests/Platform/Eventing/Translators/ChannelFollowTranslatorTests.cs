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
/// Behaviour test for the canonical translator template. It proves the consequence of translating a real
/// <c>channel.follow</c> v2 payload: a single <see cref="FollowEvent"/> is published carrying the parsed
/// follower fields, the resolved tenant, and the injected (deterministic) timestamp — not merely that a call
/// returned. Every fan-out translator mirrors this shape.
/// </summary>
public sealed class ChannelFollowTranslatorTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)
    );

    private static EventSubNotification Notification(Guid tenant, string payload)
    {
        using JsonDocument doc = JsonDocument.Parse(payload);
        return new EventSubNotification
        {
            MessageId = "msg-follow-1",
            MessageTimestamp = new DateTimeOffset(2026, 6, 20, 11, 30, 0, TimeSpan.Zero),
            SubscriptionType = "channel.follow",
            SubscriptionVersion = "2",
            BroadcasterId = tenant,
            TwitchBroadcasterUserId = "broadcaster-99",
            Event = doc.RootElement.Clone(),
        };
    }

    [Fact]
    public async Task Translate_ChannelFollow_PublishesFollowEvent_WithParsedFields()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelFollowTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                """
                {
                    "user_id": "1234",
                    "user_login": "cool_user",
                    "user_name": "Cool_User",
                    "broadcaster_user_id": "broadcaster-99",
                    "broadcaster_user_login": "streamer",
                    "broadcaster_user_name": "Streamer",
                    "followed_at": "2026-06-20T11:29:00Z"
                }
                """
            )
        );

        FollowEvent published = bus.EventsOf<FollowEvent>().Should().ContainSingle().Subject;
        published.BroadcasterId.Should().Be(tenant, "the dispatcher resolved the tenant");
        published.UserId.Should().Be("1234");
        published.UserLogin.Should().Be("cool_user");
        published.UserDisplayName.Should().Be("Cool_User");
        published
            .FollowedAt.Should()
            .Be(
                new DateTimeOffset(2026, 6, 20, 11, 29, 0, TimeSpan.Zero),
                "followed_at is parsed from the payload"
            );
        published
            .Timestamp.Should()
            .Be(Clock.GetUtcNow(), "the publisher stamps the injected clock for determinism");
    }

    [Fact]
    public async Task Translate_ChannelFollow_MissingFollowedAt_FallsBackToClock()
    {
        CapturingEventBus bus = new();
        ChannelFollowTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                Guid.NewGuid(),
                """{ "user_id": "1234", "user_login": "u", "user_name": "U" }"""
            )
        );

        FollowEvent published = bus.EventsOf<FollowEvent>().Should().ContainSingle().Subject;
        published
            .FollowedAt.Should()
            .Be(
                Clock.GetUtcNow(),
                "an absent followed_at degrades to the publish clock, never throws"
            );
    }
}

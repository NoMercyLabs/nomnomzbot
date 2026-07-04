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
/// Behaviour tests for the Guest Star (beta) fan-out translators. Each runs a realistic raw EventSub
/// <c>event</c> payload through its translator with a capturing bus and a deterministic clock, then asserts the
/// published event's concrete type and parsed fields — including the session timestamps, a guest's slot state,
/// and the settings flags + group layout.
/// </summary>
public sealed class GuestStarTranslatorsTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)
    );

    private static EventSubNotification Notification(Guid tenant, string type, string payload)
    {
        using JsonDocument doc = JsonDocument.Parse(payload);
        return new EventSubNotification
        {
            MessageId = "msg-guest-star-1",
            MessageTimestamp = new DateTimeOffset(2026, 6, 20, 11, 30, 0, TimeSpan.Zero),
            SubscriptionType = type,
            SubscriptionVersion = "beta",
            BroadcasterId = tenant,
            TwitchBroadcasterUserId = "broadcaster-99",
            Event = doc.RootElement.Clone(),
        };
    }

    [Fact]
    public async Task GuestStarSessionBegin_PublishesBeganEvent_WithSessionAndStartedAt()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelGuestStarSessionBeginTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.guest_star_session.begin",
                """
                {
                    "broadcaster_user_id": "broadcaster-99",
                    "broadcaster_user_login": "streamer",
                    "broadcaster_user_name": "Streamer",
                    "session_id": "session-2KFRQbFtpmfyD3IevNRnCzOzhg1",
                    "started_at": "2026-06-20T11:28:00Z"
                }
                """
            )
        );

        GuestStarSessionBeganEvent published = bus.EventsOf<GuestStarSessionBeganEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.SessionId.Should().Be("session-2KFRQbFtpmfyD3IevNRnCzOzhg1");
        published.StartedAt.Should().Be(new DateTimeOffset(2026, 6, 20, 11, 28, 0, TimeSpan.Zero));
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task GuestStarSessionEnd_PublishesEndedEvent_WithStartAndEndTimestamps()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelGuestStarSessionEndTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.guest_star_session.end",
                """
                {
                    "broadcaster_user_id": "broadcaster-99",
                    "session_id": "session-abc",
                    "started_at": "2026-06-20T11:28:00Z",
                    "ended_at": "2026-06-20T11:55:00Z"
                }
                """
            )
        );

        GuestStarSessionEndedEvent published = bus.EventsOf<GuestStarSessionEndedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.SessionId.Should().Be("session-abc");
        published.StartedAt.Should().Be(new DateTimeOffset(2026, 6, 20, 11, 28, 0, TimeSpan.Zero));
        published.EndedAt.Should().Be(new DateTimeOffset(2026, 6, 20, 11, 55, 0, TimeSpan.Zero));
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task GuestStarGuestUpdate_PublishesUpdatedEvent_WithGuestStateAndSlot()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelGuestStarGuestUpdateTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.guest_star_guest.update",
                """
                {
                    "broadcaster_user_id": "broadcaster-99",
                    "session_id": "session-abc",
                    "moderator_user_id": "mod-1",
                    "moderator_user_login": "mod_user",
                    "moderator_user_name": "Mod_User",
                    "guest_user_id": "guest-2",
                    "guest_user_login": "guest_streamer",
                    "guest_user_name": "Guest_Streamer",
                    "slot_id": "1",
                    "state": "live",
                    "host_video_enabled": true,
                    "host_audio_enabled": true,
                    "host_volume": 100
                }
                """
            )
        );

        GuestStarGuestUpdatedEvent published = bus.EventsOf<GuestStarGuestUpdatedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.SessionId.Should().Be("session-abc");
        published.ModeratorId.Should().Be("mod-1");
        published.GuestUserId.Should().Be("guest-2");
        published.GuestDisplayName.Should().Be("Guest_Streamer");
        published.GuestLogin.Should().Be("guest_streamer");
        published
            .State.Should()
            .Be("live", "the guest's slot state is parsed from the state field");
        published.SlotId.Should().Be("1");
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task GuestStarGuestUpdate_VacatedSlot_DegradesGuestFieldsToNull()
    {
        CapturingEventBus bus = new();
        ChannelGuestStarGuestUpdateTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                Guid.NewGuid(),
                "channel.guest_star_guest.update",
                """
                {
                    "broadcaster_user_id": "broadcaster-99",
                    "session_id": "session-abc",
                    "slot_id": "1",
                    "state": "removed"
                }
                """
            )
        );

        GuestStarGuestUpdatedEvent published = bus.EventsOf<GuestStarGuestUpdatedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.State.Should().Be("removed");
        published
            .GuestUserId.Should()
            .BeNull("a vacated slot carries no guest identity, degrades to null");
        published.GuestDisplayName.Should().BeNull();
        published.ModeratorId.Should().BeNull();
    }

    [Fact]
    public async Task GuestStarSettingsUpdate_PublishesUpdatedEvent_WithFlagsAndLayout()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelGuestStarSettingsUpdateTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.guest_star_settings.update",
                """
                {
                    "broadcaster_user_id": "broadcaster-99",
                    "is_moderator_send_live_enabled": true,
                    "slot_count": 5,
                    "is_browser_source_audio_enabled": false,
                    "group_layout": "tiled"
                }
                """
            )
        );

        GuestStarSettingsUpdatedEvent published = bus.EventsOf<GuestStarSettingsUpdatedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.IsModeratorSendLiveEnabled.Should().BeTrue();
        published.SlotCount.Should().Be(5);
        published.IsBrowserSourceAudioEnabled.Should().BeFalse();
        published.GroupLayout.Should().Be("tiled");
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }
}

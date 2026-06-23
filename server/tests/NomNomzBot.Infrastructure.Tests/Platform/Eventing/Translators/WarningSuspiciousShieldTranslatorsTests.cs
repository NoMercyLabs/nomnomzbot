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
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Infrastructure.Platform.Eventing.Translators;
using NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing.Translators;

/// <summary>
/// Behaviour tests for the warning, suspicious-user, and shield-mode translators: each proves a real EventSub
/// payload is parsed into the matching typed domain event with the resolved tenant, injected clock, and mapped
/// fields — including the nested suspicious-user <c>message</c> object, the <c>chat_rules_cited</c> array, and
/// the shield-mode timestamps.
/// </summary>
public sealed class WarningSuspiciousShieldTranslatorsTests
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
    public async Task ChannelWarningAcknowledge_PublishesWarningAcknowledgedEvent()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelWarningAcknowledgeTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.warning.acknowledge",
                """
                {
                    "user_id": "141981764",
                    "user_login": "twitchdev",
                    "user_name": "TwitchDev"
                }
                """
            )
        );

        WarningAcknowledgedEvent published = bus.EventsOf<WarningAcknowledgedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.UserId.Should().Be("141981764");
        published.UserLogin.Should().Be("twitchdev");
        published.UserDisplayName.Should().Be("TwitchDev");
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task ChannelWarningSend_PublishesWarningSentEvent_WithCitedRules()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelWarningSendTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.warning.send",
                """
                {
                    "moderator_user_id": "424596340",
                    "moderator_user_name": "quotrok",
                    "user_id": "141981764",
                    "user_login": "twitchdev",
                    "user_name": "TwitchDev",
                    "reason": "cut it out",
                    "chat_rules_cited": ["No spam", "Be kind"]
                }
                """
            )
        );

        WarningSentEvent published = bus.EventsOf<WarningSentEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.UserId.Should().Be("141981764");
        published.UserLogin.Should().Be("twitchdev");
        published.UserDisplayName.Should().Be("TwitchDev");
        published.ModeratorId.Should().Be("424596340");
        published.ModeratorDisplayName.Should().Be("quotrok");
        published.Reason.Should().Be("cut it out");
        published.ChatRulesCited.Should().Equal("No spam", "Be kind");
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task ChannelWarningSend_NullChatRulesCited_DegradesToEmptyList()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelWarningSendTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.warning.send",
                """
                {
                    "moderator_user_id": "424596340",
                    "moderator_user_name": "quotrok",
                    "user_id": "141981764",
                    "user_login": "twitchdev",
                    "user_name": "TwitchDev",
                    "reason": "cut it out",
                    "chat_rules_cited": null
                }
                """
            )
        );

        WarningSentEvent published = bus.EventsOf<WarningSentEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published
            .ChatRulesCited.Should()
            .BeEmpty("a null chat_rules_cited degrades to an empty list, never throws");
    }

    [Fact]
    public async Task ChannelSuspiciousUserMessage_PublishesEvent_WithNestedMessage()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelSuspiciousUserMessageTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.suspicious_user.message",
                """
                {
                    "broadcaster_user_id": "1050263432",
                    "user_id": "1050263434",
                    "user_login": "4a46e2cf2e2f4d6a9e6",
                    "user_name": "4a46e2cf2e2f4d6a9e6",
                    "low_trust_status": "active_monitoring",
                    "shared_ban_channel_ids": ["100", "200"],
                    "types": ["ban_evader"],
                    "ban_evasion_evaluation": "likely",
                    "message": {
                        "message_id": "101010",
                        "text": "bad stuff pogchamp",
                        "fragments": []
                    }
                }
                """
            )
        );

        SuspiciousUserMessageEvent published = bus.EventsOf<SuspiciousUserMessageEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.UserId.Should().Be("1050263434");
        published.UserLogin.Should().Be("4a46e2cf2e2f4d6a9e6");
        published.LowTrustStatus.Should().Be("active_monitoring");
        published.BanEvasionEvaluation.Should().Be("likely");
        published.MessageId.Should().Be("101010", "the message id is read from the nested object");
        published.Text.Should().Be("bad stuff pogchamp", "the text is read from the nested object");
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task ChannelSuspiciousUserUpdate_PublishesEvent_WithModeratorAndStatus()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelSuspiciousUserUpdateTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.suspicious_user.update",
                """
                {
                    "broadcaster_user_id": "1050263435",
                    "moderator_user_id": "1050263436",
                    "moderator_user_name": "29087e59dfc441968f6",
                    "user_id": "1050263437",
                    "user_login": "06fbcc75952245c5a87",
                    "user_name": "06fbcc75952245c5a87",
                    "low_trust_status": "restricted"
                }
                """
            )
        );

        SuspiciousUserUpdatedEvent published = bus.EventsOf<SuspiciousUserUpdatedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.UserId.Should().Be("1050263437");
        published.ModeratorId.Should().Be("1050263436");
        published.ModeratorDisplayName.Should().Be("29087e59dfc441968f6");
        published.LowTrustStatus.Should().Be("restricted");
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task ChannelShieldModeBegin_PublishesShieldModeBeganEvent()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelShieldModeBeginTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.shield_mode.begin",
                """
                {
                    "broadcaster_user_id": "12345",
                    "moderator_user_id": "98765",
                    "moderator_user_name": "ParticularlyParticular123",
                    "started_at": "2026-06-20T11:00:03Z"
                }
                """
            )
        );

        ShieldModeBeganEvent published = bus.EventsOf<ShieldModeBeganEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.ModeratorId.Should().Be("98765");
        published.ModeratorDisplayName.Should().Be("ParticularlyParticular123");
        published
            .StartedAt.Should()
            .Be(
                new DateTimeOffset(2026, 6, 20, 11, 0, 3, TimeSpan.Zero),
                "started_at is parsed from the payload"
            );
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task ChannelShieldModeEnd_PublishesShieldModeEndedEvent()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelShieldModeEndTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.shield_mode.end",
                """
                {
                    "broadcaster_user_id": "12345",
                    "moderator_user_id": "98765",
                    "moderator_user_name": "ParticularlyParticular123",
                    "ended_at": "2026-06-20T11:30:23Z"
                }
                """
            )
        );

        ShieldModeEndedEvent published = bus.EventsOf<ShieldModeEndedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.ModeratorId.Should().Be("98765");
        published.ModeratorDisplayName.Should().Be("ParticularlyParticular123");
        published
            .EndedAt.Should()
            .Be(
                new DateTimeOffset(2026, 6, 20, 11, 30, 23, TimeSpan.Zero),
                "ended_at is parsed from the payload"
            );
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }
}

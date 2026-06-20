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
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Infrastructure.Platform.Eventing.Translators;
using NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing.Translators;

/// <summary>
/// Behaviour tests for the ad-break / bits.use / user.update / whisper fan-out translators. Each runs a
/// realistic raw EventSub <c>event</c> payload through its translator with a capturing bus and a deterministic
/// clock, then asserts the published event's concrete type and parsed fields — including the ad-break
/// duration + automatic flag (in both the numeric and the legacy string representation), the bits type + nested
/// message text, and the whisper body nested under <c>whisper.text</c>.
/// </summary>
public sealed class AdBreakBitsUserTranslatorsTests
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
    public async Task AdBreakBegin_PublishesAdBreakBeganEvent_WithNumericDurationAndAutomaticFlag()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelAdBreakBeginTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.ad_break.begin",
                """
                {
                    "duration_seconds": 180,
                    "started_at": "2026-06-20T11:29:00Z",
                    "is_automatic": false,
                    "broadcaster_user_id": "broadcaster-99",
                    "broadcaster_user_login": "streamer",
                    "broadcaster_user_name": "Streamer",
                    "requester_user_id": "req-1",
                    "requester_user_login": "mod_user",
                    "requester_user_name": "Mod_User"
                }
                """
            )
        );

        AdBreakBeganEvent published = bus.EventsOf<AdBreakBeganEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published
            .DurationSeconds.Should()
            .Be(180, "the break length is parsed from duration_seconds");
        published.IsAutomatic.Should().BeFalse("this break was started manually by a requester");
        published.StartedAt.Should().Be(new DateTimeOffset(2026, 6, 20, 11, 29, 0, TimeSpan.Zero));
        published.RequesterUserId.Should().Be("req-1");
        published.RequesterDisplayName.Should().Be("Mod_User");
        published.Timestamp.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task AdBreakBegin_AcceptsLegacyStringDurationAndAutomatic()
    {
        CapturingEventBus bus = new();
        ChannelAdBreakBeginTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                Guid.NewGuid(),
                "channel.ad_break.begin",
                """
                {
                    "duration_seconds": "60",
                    "is_automatic": "true",
                    "started_at": "2026-06-20T11:29:00Z"
                }
                """
            )
        );

        AdBreakBeganEvent published = bus.EventsOf<AdBreakBeganEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published
            .DurationSeconds.Should()
            .Be(60, "a string duration_seconds is parsed to its int value");
        published
            .IsAutomatic.Should()
            .BeTrue("the legacy string \"true\" parses to the automatic flag");
        published.RequesterUserId.Should().BeNull("an automatic break carries no requester");
    }

    [Fact]
    public async Task BitsUse_PublishesBitsUsedEvent_WithCheerTypeAndMessageText()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelBitsUseTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.bits.use",
                """
                {
                    "user_id": "9001",
                    "user_login": "cheerer",
                    "user_name": "Cheerer",
                    "broadcaster_user_id": "broadcaster-99",
                    "broadcaster_user_login": "streamer",
                    "broadcaster_user_name": "Streamer",
                    "bits": 100,
                    "type": "cheer",
                    "power_up": null,
                    "message": {
                        "text": "Cheer100 take my bits!",
                        "fragments": [
                            { "type": "cheermote", "text": "Cheer100", "cheermote": { "prefix": "Cheer", "bits": 100, "tier": 1 }, "emote": null },
                            { "type": "text", "text": " take my bits!", "cheermote": null, "emote": null }
                        ]
                    }
                }
                """
            )
        );

        BitsUsedEvent published = bus.EventsOf<BitsUsedEvent>().Should().ContainSingle().Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.UserId.Should().Be("9001");
        published.UserDisplayName.Should().Be("Cheerer");
        published.UserLogin.Should().Be("cheerer");
        published.Bits.Should().Be(100, "the bits amount is parsed from the top-level bits field");
        published.Type.Should().Be("cheer");
        published
            .MessageText.Should()
            .Be("Cheer100 take my bits!", "the chat text is read from the nested message object");
        published.Timestamp.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task BitsUse_PowerUpWithoutMessage_DegradesMessageTextToNull()
    {
        CapturingEventBus bus = new();
        ChannelBitsUseTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                Guid.NewGuid(),
                "channel.bits.use",
                """
                {
                    "user_id": "9001",
                    "user_login": "redeemer",
                    "user_name": "Redeemer",
                    "bits": 500,
                    "type": "power_up"
                }
                """
            )
        );

        BitsUsedEvent published = bus.EventsOf<BitsUsedEvent>().Should().ContainSingle().Subject;
        published.Type.Should().Be("power_up");
        published.Bits.Should().Be(500);
        published
            .MessageText.Should()
            .BeNull("a bare power-up carries no message object, so the text degrades to null");
    }

    [Fact]
    public async Task UserUpdate_PublishesUserUpdatedEvent_WithEmailWhenScopeGranted()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        UserUpdateTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "user.update",
                """
                {
                    "user_id": "9001",
                    "user_login": "the_user",
                    "user_name": "The_User",
                    "email": "user@example.com",
                    "email_verified": true,
                    "description": "Just a streamer."
                }
                """
            )
        );

        UserUpdatedEvent published = bus.EventsOf<UserUpdatedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant, "the dispatcher resolved the user tenant");
        published.UserId.Should().Be("9001");
        published.UserLogin.Should().Be("the_user");
        published.UserDisplayName.Should().Be("The_User");
        published
            .Email.Should()
            .Be("user@example.com", "email is present with the user:read:email scope");
        published.Description.Should().Be("Just a streamer.");
        published.Timestamp.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task UserUpdate_WithoutEmailScope_DegradesEmailToNull()
    {
        CapturingEventBus bus = new();
        UserUpdateTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                Guid.NewGuid(),
                "user.update",
                """
                {
                    "user_id": "9001",
                    "user_login": "the_user",
                    "user_name": "The_User",
                    "description": ""
                }
                """
            )
        );

        UserUpdatedEvent published = bus.EventsOf<UserUpdatedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published
            .Email.Should()
            .BeNull("without the user:read:email scope Twitch omits email, which degrades to null");
        published.Description.Should().BeEmpty();
    }

    [Fact]
    public async Task WhisperMessage_PublishesWhisperReceivedEvent_WithNestedText()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        UserWhisperMessageTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "user.whisper.message",
                """
                {
                    "from_user_id": "12826",
                    "from_user_login": "twitch",
                    "from_user_name": "Twitch",
                    "to_user_id": "141981764",
                    "to_user_login": "twitchdev",
                    "to_user_name": "TwitchDev",
                    "whisper_id": "3c4719ba-fe16-4c75-8f00-78142a375cf1",
                    "whisper": { "text": "I have a secret to tell you!" }
                }
                """
            )
        );

        WhisperReceivedEvent published = bus.EventsOf<WhisperReceivedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.WhisperId.Should().Be("3c4719ba-fe16-4c75-8f00-78142a375cf1");
        published.FromUserId.Should().Be("12826");
        published.FromUserDisplayName.Should().Be("Twitch");
        published.FromUserLogin.Should().Be("twitch");
        published.ToUserId.Should().Be("141981764");
        published
            .Text.Should()
            .Be(
                "I have a secret to tell you!",
                "the body is read from the nested whisper.text field"
            );
        published.Timestamp.Should().Be(Clock.GetUtcNow());
    }
}

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
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Infrastructure.Platform.Eventing.Translators;
using NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing.Translators;

/// <summary>
/// Behaviour tests for the chat fan-out translators. Each feeds a realistic raw EventSub payload through a
/// translator and asserts the consequence: the correct domain event type published, carrying the parsed fields,
/// the resolved tenant, and the injected clock. The hot-path <c>channel.chat.message</c> translator is exercised
/// across fragments, badge→role derivation, a reply, and a cheer.
/// </summary>
public sealed class ChatTranslatorsTests
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
    public async Task ChatMessage_PlainText_PublishesReceivedEvent_WithFragmentsAndTenant()
    {
        CapturingEventBus bus = new();
        ChannelChatMessageTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                "channel.chat.message",
                """
                {
                    "broadcaster_user_id": "broadcaster-99",
                    "chatter_user_id": "555",
                    "chatter_user_login": "cool_user",
                    "chatter_user_name": "Cool_User",
                    "message_id": "abc-123",
                    "color": "#FF0000",
                    "message_type": "text",
                    "message": {
                        "text": "hello world Kappa",
                        "fragments": [
                            { "type": "text", "text": "hello world " },
                            {
                                "type": "emote",
                                "text": "Kappa",
                                "emote": { "id": "25", "emote_set_id": "0", "owner_id": "twitch", "format": ["static", "animated"] }
                            }
                        ]
                    },
                    "badges": []
                }
                """
            )
        );

        ChatMessageReceivedEvent published = bus.EventsOf<ChatMessageReceivedEvent>()
            .Should()
            .ContainSingle()
            .Subject;

        published.BroadcasterId.Should().Be(Tenant, "the dispatcher resolved the tenant");
        published.TwitchBroadcasterId.Should().Be("broadcaster-99");
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
        published.MessageId.Should().Be("abc-123");
        published.UserId.Should().Be("555");
        published.UserLogin.Should().Be("cool_user");
        published.UserDisplayName.Should().Be("Cool_User");
        published.Message.Should().Be("hello world Kappa");
        published.ColorHex.Should().Be("#FF0000");
        published.MessageType.Should().Be("text");
        published.Bits.Should().Be(0);

        published.Fragments.Should().HaveCount(2);
        published.Fragments[0].Type.Should().Be("text");
        published.Fragments[0].Text.Should().Be("hello world ");

        ChatMessageFragment emote = published.Fragments[1];
        emote.Type.Should().Be("emote");
        emote.Text.Should().Be("Kappa");
        emote.EmoteId.Should().Be("25");
        emote.EmoteSetId.Should().Be("0");
        emote.EmoteOwnerId.Should().Be("twitch");
        emote.EmoteFormats.Should().Equal("static", "animated");
    }

    [Fact]
    public async Task ChatMessage_DerivesRoleFlags_FromBadgeSetIds()
    {
        CapturingEventBus bus = new();
        ChannelChatMessageTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                "channel.chat.message",
                """
                {
                    "chatter_user_id": "1",
                    "chatter_user_login": "mod_sub",
                    "chatter_user_name": "Mod_Sub",
                    "message_id": "m2",
                    "message": { "text": "hi", "fragments": [ { "type": "text", "text": "hi" } ] },
                    "badges": [
                        { "set_id": "moderator", "id": "1", "info": "" },
                        { "set_id": "subscriber", "id": "12", "info": "12" }
                    ]
                }
                """
            )
        );

        ChatMessageReceivedEvent published = bus.EventsOf<ChatMessageReceivedEvent>()
            .Should()
            .ContainSingle()
            .Subject;

        published.Badges.Should().HaveCount(2);
        published.Badges[0].Should().Be(new ChatBadge("moderator", "1", ""));
        published.Badges[1].Should().Be(new ChatBadge("subscriber", "12", "12"));
        published.IsModerator.Should().BeTrue("a moderator badge is present");
        published.IsSubscriber.Should().BeTrue("a subscriber badge is present");
        published.IsVip.Should().BeFalse();
        published.IsBroadcaster.Should().BeFalse();
    }

    [Fact]
    public async Task ChatMessage_FounderBadge_CountsAsSubscriber()
    {
        CapturingEventBus bus = new();
        ChannelChatMessageTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                "channel.chat.message",
                """
                {
                    "chatter_user_id": "1",
                    "chatter_user_login": "founder_user",
                    "chatter_user_name": "Founder_User",
                    "message_id": "m3",
                    "message": { "text": "yo", "fragments": [ { "type": "text", "text": "yo" } ] },
                    "badges": [
                        { "set_id": "broadcaster", "id": "1" },
                        { "set_id": "founder", "id": "0" }
                    ]
                }
                """
            )
        );

        ChatMessageReceivedEvent published = bus.EventsOf<ChatMessageReceivedEvent>()
            .Should()
            .ContainSingle()
            .Subject;

        published.IsBroadcaster.Should().BeTrue();
        published.IsSubscriber.Should().BeTrue("a founder badge implies subscriber status");
    }

    [Fact]
    public async Task ChatMessage_Reply_PublishesParentFields()
    {
        CapturingEventBus bus = new();
        ChannelChatMessageTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                "channel.chat.message",
                """
                {
                    "chatter_user_id": "9",
                    "chatter_user_login": "replier",
                    "chatter_user_name": "Replier",
                    "message_id": "reply-1",
                    "message": { "text": "@Streamer nice", "fragments": [ { "type": "text", "text": "@Streamer nice" } ] },
                    "badges": [],
                    "reply": {
                        "parent_message_id": "parent-77",
                        "parent_message_body": "original message",
                        "parent_user_id": "100",
                        "parent_user_name": "Streamer",
                        "parent_user_login": "streamer"
                    }
                }
                """
            )
        );

        ChatMessageReceivedEvent published = bus.EventsOf<ChatMessageReceivedEvent>()
            .Should()
            .ContainSingle()
            .Subject;

        published.ReplyParentMessageId.Should().Be("parent-77");
        published.ReplyParentMessageBody.Should().Be("original message");
        published.ReplyParentUserName.Should().Be("Streamer");
    }

    [Fact]
    public async Task ChatMessage_Cheer_PublishesBits()
    {
        CapturingEventBus bus = new();
        ChannelChatMessageTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                "channel.chat.message",
                """
                {
                    "chatter_user_id": "7",
                    "chatter_user_login": "cheerer",
                    "chatter_user_name": "Cheerer",
                    "message_id": "cheer-1",
                    "message": {
                        "text": "Cheer100 nice stream",
                        "fragments": [
                            {
                                "type": "cheermote",
                                "text": "Cheer100",
                                "cheermote": { "prefix": "Cheer", "bits": 100, "tier": 1 }
                            },
                            { "type": "text", "text": " nice stream" }
                        ]
                    },
                    "badges": [],
                    "cheer": { "bits": 100 }
                }
                """
            )
        );

        ChatMessageReceivedEvent published = bus.EventsOf<ChatMessageReceivedEvent>()
            .Should()
            .ContainSingle()
            .Subject;

        published.Bits.Should().Be(100);
        published.Fragments[0].Type.Should().Be("cheermote");
        published.Fragments[0].CheermotePrefix.Should().Be("Cheer");
        published.Fragments[0].CheermoteBits.Should().Be(100);
        published.Fragments[0].CheermoteTier.Should().Be(1);
    }

    [Fact]
    public async Task ChatMessage_Mention_PublishesMentionFragment()
    {
        CapturingEventBus bus = new();
        ChannelChatMessageTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                "channel.chat.message",
                """
                {
                    "chatter_user_id": "8",
                    "chatter_user_login": "mentioner",
                    "chatter_user_name": "Mentioner",
                    "message_id": "mention-1",
                    "message": {
                        "text": "@Friend hi",
                        "fragments": [
                            {
                                "type": "mention",
                                "text": "@Friend",
                                "mention": { "user_id": "200", "user_login": "friend", "user_name": "Friend" }
                            },
                            { "type": "text", "text": " hi" }
                        ]
                    },
                    "badges": []
                }
                """
            )
        );

        ChatMessageReceivedEvent published = bus.EventsOf<ChatMessageReceivedEvent>()
            .Should()
            .ContainSingle()
            .Subject;

        ChatMessageFragment mention = published.Fragments[0];
        mention.Type.Should().Be("mention");
        mention.MentionUserId.Should().Be("200");
        mention.MentionUserLogin.Should().Be("friend");
        mention.MentionUserName.Should().Be("Friend");
    }

    [Fact]
    public async Task ChatMessage_NoTextField_ConcatenatesFragmentTexts()
    {
        CapturingEventBus bus = new();
        ChannelChatMessageTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                "channel.chat.message",
                """
                {
                    "chatter_user_id": "8",
                    "chatter_user_login": "u",
                    "chatter_user_name": "U",
                    "message_id": "concat-1",
                    "message": {
                        "fragments": [
                            { "type": "text", "text": "part one " },
                            { "type": "text", "text": "part two" }
                        ]
                    },
                    "badges": []
                }
                """
            )
        );

        ChatMessageReceivedEvent published = bus.EventsOf<ChatMessageReceivedEvent>()
            .Should()
            .ContainSingle()
            .Subject;

        published
            .Message.Should()
            .Be("part one part two", "an absent text field falls back to joined fragment texts");
    }

    [Fact]
    public async Task ChatMessageDelete_PublishesDeletedEvent_WithTarget()
    {
        CapturingEventBus bus = new();
        ChannelChatMessageDeleteTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                "channel.chat.message_delete",
                """
                {
                    "broadcaster_user_id": "broadcaster-99",
                    "target_user_id": "321",
                    "target_user_name": "Naughty",
                    "target_user_login": "naughty",
                    "message_id": "del-1"
                }
                """
            )
        );

        ChatMessageDeletedEvent published = bus.EventsOf<ChatMessageDeletedEvent>()
            .Should()
            .ContainSingle()
            .Subject;

        published.BroadcasterId.Should().Be(Tenant);
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
        published.MessageId.Should().Be("del-1");
        published.TargetUserId.Should().Be("321");
    }

    [Fact]
    public async Task ChatClear_PublishesClearedEvent()
    {
        CapturingEventBus bus = new();
        ChannelChatClearTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                "channel.chat.clear",
                """{ "broadcaster_user_id": "broadcaster-99", "broadcaster_user_login": "streamer", "broadcaster_user_name": "Streamer" }"""
            )
        );

        ChatClearedEvent published = bus.EventsOf<ChatClearedEvent>()
            .Should()
            .ContainSingle()
            .Subject;

        published.BroadcasterId.Should().Be(Tenant);
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task ChatClearUserMessages_PublishesTargetedClearEvent()
    {
        CapturingEventBus bus = new();
        ChannelChatClearUserMessagesTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                "channel.chat.clear_user_messages",
                """
                {
                    "broadcaster_user_id": "broadcaster-99",
                    "target_user_id": "777",
                    "target_user_name": "Spammer",
                    "target_user_login": "spammer"
                }
                """
            )
        );

        ChatUserMessagesClearedEvent published = bus.EventsOf<ChatUserMessagesClearedEvent>()
            .Should()
            .ContainSingle()
            .Subject;

        published.BroadcasterId.Should().Be(Tenant);
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
        published.TargetUserId.Should().Be("777");
        published.TargetUserDisplayName.Should().Be("Spammer");
        published.TargetUserLogin.Should().Be("spammer");
    }

    [Fact]
    public async Task ChatNotification_Resub_PublishesNoticeWithSystemMessageAndText()
    {
        CapturingEventBus bus = new();
        ChannelChatNotificationTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                "channel.chat.notification",
                """
                {
                    "broadcaster_user_id": "broadcaster-99",
                    "chatter_user_id": "42",
                    "chatter_user_login": "loyal_fan",
                    "chatter_user_name": "Loyal_Fan",
                    "chatter_is_anonymous": false,
                    "notice_type": "resub",
                    "system_message": "Loyal_Fan subscribed at Tier 1. They've subscribed for 12 months!",
                    "message_id": "notif-1",
                    "message": {
                        "text": "love this stream",
                        "fragments": [ { "type": "text", "text": "love this stream" } ]
                    }
                }
                """
            )
        );

        ChatNotificationEvent published = bus.EventsOf<ChatNotificationEvent>()
            .Should()
            .ContainSingle()
            .Subject;

        published.BroadcasterId.Should().Be(Tenant);
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
        published.ChatterUserId.Should().Be("42");
        published.ChatterDisplayName.Should().Be("Loyal_Fan");
        published.ChatterLogin.Should().Be("loyal_fan");
        published.IsAnonymous.Should().BeFalse();
        published.NoticeType.Should().Be("resub");
        published
            .SystemMessage.Should()
            .Be("Loyal_Fan subscribed at Tier 1. They've subscribed for 12 months!");
        published.MessageText.Should().Be("love this stream");
        published.MessageId.Should().Be("notif-1");
    }

    [Fact]
    public async Task ChatNotification_AnonymousGift_PublishesAnonymousFlag()
    {
        CapturingEventBus bus = new();
        ChannelChatNotificationTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                "channel.chat.notification",
                """
                {
                    "chatter_user_id": "",
                    "chatter_user_login": "",
                    "chatter_user_name": "",
                    "chatter_is_anonymous": true,
                    "notice_type": "community_sub_gift",
                    "system_message": "An anonymous user gifted 5 subs!",
                    "message_id": "notif-2",
                    "message": { "text": "", "fragments": [] }
                }
                """
            )
        );

        ChatNotificationEvent published = bus.EventsOf<ChatNotificationEvent>()
            .Should()
            .ContainSingle()
            .Subject;

        published.IsAnonymous.Should().BeTrue();
        published.NoticeType.Should().Be("community_sub_gift");
        published.MessageText.Should().BeEmpty();
    }

    [Fact]
    public async Task ChatSettingsUpdate_ModesOn_PublishesDurations()
    {
        CapturingEventBus bus = new();
        ChannelChatSettingsUpdateTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                "channel.chat_settings.update",
                """
                {
                    "broadcaster_user_id": "broadcaster-99",
                    "emote_mode": true,
                    "follower_mode": true,
                    "follower_mode_duration_minutes": 30,
                    "slow_mode": true,
                    "slow_mode_wait_time_seconds": 10,
                    "subscriber_mode": false,
                    "unique_chat_mode": true
                }
                """
            )
        );

        ChatSettingsUpdatedEvent published = bus.EventsOf<ChatSettingsUpdatedEvent>()
            .Should()
            .ContainSingle()
            .Subject;

        published.BroadcasterId.Should().Be(Tenant);
        published.EmoteMode.Should().BeTrue();
        published.FollowerMode.Should().BeTrue();
        published.FollowerModeDurationMinutes.Should().Be(30);
        published.SlowMode.Should().BeTrue();
        published.SlowModeWaitSeconds.Should().Be(10);
        published.SubscriberMode.Should().BeFalse();
        published.UniqueChatMode.Should().BeTrue();
    }

    [Fact]
    public async Task ChatSettingsUpdate_ModesOff_NullsDurations()
    {
        CapturingEventBus bus = new();
        ChannelChatSettingsUpdateTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                "channel.chat_settings.update",
                """
                {
                    "broadcaster_user_id": "broadcaster-99",
                    "emote_mode": false,
                    "follower_mode": false,
                    "slow_mode": false,
                    "subscriber_mode": false,
                    "unique_chat_mode": false
                }
                """
            )
        );

        ChatSettingsUpdatedEvent published = bus.EventsOf<ChatSettingsUpdatedEvent>()
            .Should()
            .ContainSingle()
            .Subject;

        published.FollowerMode.Should().BeFalse();
        published
            .FollowerModeDurationMinutes.Should()
            .BeNull("the duration is null when follower mode is off");
        published.SlowMode.Should().BeFalse();
        published.SlowModeWaitSeconds.Should().BeNull("the wait is null when slow mode is off");
    }

    [Fact]
    public async Task ChatUserMessageHold_PublishesHeldEvent()
    {
        CapturingEventBus bus = new();
        ChannelChatUserMessageHoldTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                "channel.chat.user_message_hold",
                """
                {
                    "broadcaster_user_id": "broadcaster-99",
                    "user_id": "888",
                    "user_login": "held_user",
                    "user_name": "Held_User",
                    "message_id": "hold-1",
                    "message": { "text": "suspicious text", "fragments": [ { "type": "text", "text": "suspicious text" } ] }
                }
                """
            )
        );

        ChatUserMessageHeldEvent published = bus.EventsOf<ChatUserMessageHeldEvent>()
            .Should()
            .ContainSingle()
            .Subject;

        published.BroadcasterId.Should().Be(Tenant);
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
        published.UserId.Should().Be("888");
        published.UserDisplayName.Should().Be("Held_User");
        published.UserLogin.Should().Be("held_user");
        published.MessageId.Should().Be("hold-1");
        published.Text.Should().Be("suspicious text");
    }

    [Fact]
    public async Task ChatUserMessageUpdate_PublishesUpdatedEvent_WithStatus()
    {
        CapturingEventBus bus = new();
        ChannelChatUserMessageUpdateTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                "channel.chat.user_message_update",
                """
                {
                    "broadcaster_user_id": "broadcaster-99",
                    "user_id": "888",
                    "user_login": "held_user",
                    "user_name": "Held_User",
                    "status": "approved",
                    "message_id": "hold-1",
                    "message": { "text": "suspicious text", "fragments": [ { "type": "text", "text": "suspicious text" } ] }
                }
                """
            )
        );

        ChatUserMessageUpdatedEvent published = bus.EventsOf<ChatUserMessageUpdatedEvent>()
            .Should()
            .ContainSingle()
            .Subject;

        published.BroadcasterId.Should().Be(Tenant);
        published.Status.Should().Be("approved");
        published.MessageId.Should().Be("hold-1");
        published.UserLogin.Should().Be("held_user");
        published.Text.Should().Be("suspicious text");
    }

    [Fact]
    public async Task ChatNotification_WatchStreak_PublishesWatchStreakReceivedEvent()
    {
        CapturingEventBus bus = new();
        ChannelChatNotificationTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                "channel.chat.notification",
                """
                {
                    "broadcaster_user_id": "broadcaster-99",
                    "chatter_user_id": "555",
                    "chatter_user_login": "cool_user",
                    "chatter_user_name": "Cool_User",
                    "chatter_is_anonymous": false,
                    "message_id": "n-1",
                    "message": { "text": "", "fragments": [] },
                    "notice_type": "watch_streak",
                    "system_message": "Cool_User watched 12 streams in a row!",
                    "watch_streak": { "streak_count": 12, "channel_points_awarded": 350 }
                }
                """
            )
        );

        WatchStreakReceivedEvent published = bus.EventsOf<WatchStreakReceivedEvent>()
            .Should()
            .ContainSingle()
            .Subject;

        published.BroadcasterId.Should().Be(Tenant);
        published.UserDisplayName.Should().Be("Cool_User");
        published.UserLogin.Should().Be("cool_user");
        published.StreakMonths.Should().Be(12);
        published.ChannelPointsEarned.Should().Be(350);
    }

    [Fact]
    public async Task ChatNotification_NonWatchStreak_DoesNotPublishWatchStreak()
    {
        CapturingEventBus bus = new();
        ChannelChatNotificationTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                "channel.chat.notification",
                """
                {
                    "broadcaster_user_id": "broadcaster-99",
                    "chatter_user_id": "555",
                    "chatter_user_login": "cool_user",
                    "chatter_user_name": "Cool_User",
                    "chatter_is_anonymous": false,
                    "message_id": "n-2",
                    "message": { "text": "", "fragments": [] },
                    "notice_type": "raid",
                    "system_message": "Raiders incoming!"
                }
                """
            )
        );

        bus.EventsOf<WatchStreakReceivedEvent>().Should().BeEmpty();
        bus.EventsOf<ChatNotificationEvent>().Should().ContainSingle();
    }
}

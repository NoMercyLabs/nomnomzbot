// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.DevPlatform;

/// <summary>
/// Real EventSub wire payloads, keyed by the SDK event catalog's own <see cref="EventDescriptor.WireName"/> —
/// NOT the raw Twitch EventSub subscription type (e.g. <c>channel.follow</c>). Domain events without an
/// <c>[Event("…")]</c> override get their catalog wire name from <see cref="EventCatalog.DeriveWireName"/>
/// (module-from-namespace + PascalCase-split type name), so <c>FollowEvent</c> (Domain.Community.Events)
/// catalogs as <c>community.follow</c>, not <c>channel.follow</c>. Each value below is copied verbatim from the
/// raw-string fixture used in the corresponding translator's own behaviour test — the verified-real Twitch wire
/// shape, not a hand-written approximation. Only events with a fixture proven against a real translator test
/// appear here (external EventSub-derived events). Every other event is an internal domain event with no wire
/// format to translate from — its C# type IS the ground truth, so <see cref="SdkTypeEmitter.EmitEventCatalog"/>
/// falls back to <see cref="ReflectionSampleGenerator"/> for those, reflecting the same type the JSON Schema is
/// built from rather than a hand-guessed payload.
/// </summary>
/// <remarks>
/// TODO: attach a verified real fixture here for any external EventSub event as its translator test is written or
/// located, moving it out of the reflection-generated fallback. Never fabricate an external wire payload by hand —
/// either copy it from a real translator fixture (this dictionary) or let the reflection fallback generate it from
/// the type itself when there is no external wire format to copy from.
/// </remarks>
public static class EventSamplePayloads
{
    public static readonly IReadOnlyDictionary<string, string> ByWireName = new Dictionary<
        string,
        string
    >
    {
        // ChannelFollowTranslatorTests.Translate_ChannelFollow_PublishesFollowEvent_WithParsedFields
        // Twitch subscription type: channel.follow. Catalog wire name: FollowEvent (Domain.Community.Events)
        // derives to "community.follow" (module "community", no [Event] override).
        ["community.follow"] = """
            {
                "user_id": "1234",
                "user_login": "cool_user",
                "user_name": "Cool_User",
                "broadcaster_user_id": "broadcaster-99",
                "broadcaster_user_login": "streamer",
                "broadcaster_user_name": "Streamer",
                "followed_at": "2026-06-20T11:29:00Z"
            }
            """,

        // ChatTranslatorsTests.ChatMessage_PlainText_PublishesReceivedEvent_WithFragmentsAndTenant
        // Twitch subscription type: channel.chat.message. Catalog wire name: ChatMessageReceivedEvent
        // (Domain.Chat.Events) carries an [Event("chat.message", …)] override.
        ["chat.message"] = """
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
            """,

        // ChatTranslatorsTests.ChatMessageDelete_PublishesDeletedEvent_WithTarget
        // Twitch subscription type: channel.chat.message_delete. Catalog wire name: ChatMessageDeletedEvent
        // (Domain.Chat.Events) derives to "chat.message.deleted" (module "chat", leading word "chat" dropped).
        ["chat.message.deleted"] = """
            {
                "broadcaster_user_id": "broadcaster-99",
                "target_user_id": "321",
                "target_user_name": "Naughty",
                "target_user_login": "naughty",
                "message_id": "del-1"
            }
            """,

        // ChatTranslatorsTests.ChatClear_PublishesClearedEvent
        // Twitch subscription type: channel.chat.clear. Catalog wire name: ChatClearedEvent (Domain.Chat.Events)
        // derives to "chat.cleared".
        ["chat.cleared"] = """
            { "broadcaster_user_id": "broadcaster-99", "broadcaster_user_login": "streamer", "broadcaster_user_name": "Streamer" }
            """,

        // ChatTranslatorsTests.ChatClearUserMessages_PublishesTargetedClearEvent
        // Twitch subscription type: channel.chat.clear_user_messages. Catalog wire name:
        // ChatUserMessagesClearedEvent (Domain.Chat.Events) derives to "chat.user.messages.cleared".
        ["chat.user.messages.cleared"] = """
            {
                "broadcaster_user_id": "broadcaster-99",
                "target_user_id": "777",
                "target_user_name": "Spammer",
                "target_user_login": "spammer"
            }
            """,

        // ChatTranslatorsTests.ChatNotification_Resub_PublishesNoticeWithSystemMessageAndText
        // Twitch subscription type: channel.chat.notification. Catalog wire name: ChatNotificationEvent
        // (Domain.Chat.Events) derives to "chat.notification".
        ["chat.notification"] = """
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
            """,

        // ChatTranslatorsTests.ChatSettingsUpdate_ModesOn_PublishesDurations
        // Twitch subscription type: channel.chat_settings.update. Catalog wire name: ChatSettingsUpdatedEvent
        // (Domain.Chat.Events) derives to "chat.settings.updated".
        ["chat.settings.updated"] = """
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
            """,

        // ChatTranslatorsTests.ChatUserMessageHold_PublishesHeldEvent
        // Twitch subscription type: channel.chat.user_message_hold. Catalog wire name: ChatUserMessageHeldEvent
        // (Domain.Chat.Events) derives to "chat.user.message.held".
        ["chat.user.message.held"] = """
            {
                "broadcaster_user_id": "broadcaster-99",
                "user_id": "888",
                "user_login": "held_user",
                "user_name": "Held_User",
                "message_id": "hold-1",
                "message": { "text": "suspicious text", "fragments": [ { "type": "text", "text": "suspicious text" } ] }
            }
            """,

        // ChatTranslatorsTests.ChatUserMessageUpdate_PublishesUpdatedEvent_WithStatus
        // Twitch subscription type: channel.chat.user_message_update. Catalog wire name:
        // ChatUserMessageUpdatedEvent (Domain.Chat.Events) derives to "chat.user.message.updated".
        ["chat.user.message.updated"] = """
            {
                "broadcaster_user_id": "broadcaster-99",
                "user_id": "888",
                "user_login": "held_user",
                "user_name": "Held_User",
                "status": "approved",
                "message_id": "hold-1",
                "message": { "text": "suspicious text", "fragments": [ { "type": "text", "text": "suspicious text" } ] }
            }
            """,

        // ChatTranslatorsTests.ChatNotification_WatchStreak_PublishesWatchStreakReceivedEvent
        // Twitch subscription type: channel.chat.notification (notice_type "watch_streak", fanned out a second
        // time). Catalog wire name: WatchStreakReceivedEvent (Domain.Rewards.Events) derives to
        // "rewards.watch.streak.received" (module "rewards", no leading-word match to drop).
        ["rewards.watch.streak.received"] = """
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
            """,

        // SharedChatTranslatorsTests.SharedChatBegin_PublishesSharedChatBeganEvent_WithHostAndParticipants
        // Twitch subscription type: channel.shared_chat.begin. Catalog wire name: SharedChatBeganEvent
        // (Domain.Chat.Events) derives to "chat.shared.chat.began" (module "chat", leading word "shared" kept).
        ["chat.shared.chat.began"] = """
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
            """,

        // SharedChatTranslatorsTests.SharedChatUpdate_PublishesSharedChatUpdatedEvent_WithCurrentParticipants
        // Twitch subscription type: channel.shared_chat.update. Catalog wire name: SharedChatUpdatedEvent
        // (Domain.Chat.Events) derives to "chat.shared.chat.updated".
        ["chat.shared.chat.updated"] = """
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
            """,

        // SharedChatTranslatorsTests.SharedChatEnd_PublishesSharedChatEndedEvent_WithSessionAndHost
        // Twitch subscription type: channel.shared_chat.end. Catalog wire name: SharedChatEndedEvent
        // (Domain.Chat.Events) derives to "chat.shared.chat.ended".
        ["chat.shared.chat.ended"] = """
            {
                "session_id": "session-abc",
                "broadcaster_user_id": "broadcaster-99",
                "host_broadcaster_user_id": "host-1",
                "host_broadcaster_user_login": "host_streamer",
                "host_broadcaster_user_name": "Host_Streamer"
            }
            """,

        // AdBreakBitsUserTranslatorsTests.WhisperMessage_PublishesWhisperReceivedEvent_WithNestedText
        // Twitch subscription type: user.whisper.message. Catalog wire name: WhisperReceivedEvent
        // (Domain.Chat.Events) derives to "chat.whisper.received".
        ["chat.whisper.received"] = """
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
            """,

        // AutoModTranslatorsTests.Translate_AutoModMessageHold_PublishesHeldEvent_ConcatenatingFragments
        // Twitch subscription type: automod.message.hold. Catalog wire name: AutoModMessageHeldEvent
        // (Domain.Moderation.Events) derives to "moderation.auto.mod.message.held" — the PascalCase splitter
        // breaks "AutoMod" into "auto"+"mod" (no acronym exemption in the algorithm), so the leading word
        // "auto" never matches the "moderation" module and is never dropped.
        ["moderation.auto.mod.message.held"] = """
            {
                "broadcaster_user_id": "broadcaster-99",
                "broadcaster_user_login": "streamer",
                "broadcaster_user_name": "Streamer",
                "user_id": "4242",
                "user_login": "rude_user",
                "user_name": "Rude_User",
                "message_id": "held-msg-1",
                "message": {
                    "text": "",
                    "fragments": [
                        { "type": "text", "text": "you are ", "cheermote": null, "emote": null },
                        { "type": "text", "text": "such a problem", "cheermote": null, "emote": null }
                    ]
                },
                "reason": "automod",
                "automod": {
                    "category": "bullying",
                    "level": 4,
                    "boundaries": [{ "start_pos": 0, "end_pos": 20 }]
                },
                "blocked_term": null,
                "held_at": "2026-06-20T11:29:30Z"
            }
            """,

        // AutoModTranslatorsTests.Translate_AutoModMessageUpdate_PublishesUpdatedEvent_WithStatusAndModerator
        // Twitch subscription type: automod.message.update. Catalog wire name: AutoModMessageUpdatedEvent
        // (Domain.Moderation.Events) derives to "moderation.auto.mod.message.updated" (same split as above).
        ["moderation.auto.mod.message.updated"] = """
            {
                "broadcaster_user_id": "broadcaster-99",
                "moderator_user_id": "mod-7",
                "moderator_user_login": "cool_mod",
                "moderator_user_name": "Cool_Mod",
                "user_id": "4242",
                "user_login": "rude_user",
                "user_name": "Rude_User",
                "message_id": "held-msg-1",
                "message": { "text": "you are such a problem", "fragments": [] },
                "status": "denied",
                "held_at": "2026-06-20T11:29:30Z"
            }
            """,

        // AutoModTranslatorsTests.Translate_AutoModSettingsUpdate_PublishesSettingsEvent_WithCategoryLevels
        // Twitch subscription type: automod.settings.update. Catalog wire name: AutoModSettingsUpdatedEvent
        // (Domain.Moderation.Events) derives to "moderation.auto.mod.settings.updated".
        ["moderation.auto.mod.settings.updated"] = """
            {
                "broadcaster_user_id": "broadcaster-99",
                "broadcaster_user_login": "streamer",
                "broadcaster_user_name": "Streamer",
                "moderator_user_id": "mod-7",
                "moderator_user_login": "cool_mod",
                "moderator_user_name": "Cool_Mod",
                "overall_level": null,
                "disability": 1,
                "aggression": 2,
                "sexuality_sex_or_gender": 3,
                "misogyny": 4,
                "bullying": 5,
                "swearing": 0,
                "race_ethnicity_or_religion": 6,
                "sex_based_terms": 7
            }
            """,

        // AutoModTranslatorsTests.Translate_AutoModTermsUpdate_PublishesTermsEvent_WithTermList
        // Twitch subscription type: automod.terms.update. Catalog wire name: AutoModTermsUpdatedEvent
        // (Domain.Moderation.Events) derives to "moderation.auto.mod.terms.updated".
        ["moderation.auto.mod.terms.updated"] = """
            {
                "broadcaster_user_id": "broadcaster-99",
                "broadcaster_user_login": "streamer",
                "broadcaster_user_name": "Streamer",
                "moderator_user_id": "mod-7",
                "moderator_user_login": "cool_mod",
                "moderator_user_name": "Cool_Mod",
                "action": "add_blocked",
                "from_automod": true,
                "terms": ["badword", "anotherword", "thirdword"]
            }
            """,

        // WarningSuspiciousShieldTranslatorsTests.ChannelWarningAcknowledge_PublishesWarningAcknowledgedEvent
        // Twitch subscription type: channel.warning.acknowledge. Catalog wire name: WarningAcknowledgedEvent
        // (Domain.Moderation.Events) derives to "moderation.warning.acknowledged".
        ["moderation.warning.acknowledged"] = """
            {
                "user_id": "141981764",
                "user_login": "twitchdev",
                "user_name": "TwitchDev"
            }
            """,

        // WarningSuspiciousShieldTranslatorsTests.ChannelWarningSend_PublishesWarningSentEvent_WithCitedRules
        // Twitch subscription type: channel.warning.send. Catalog wire name: WarningSentEvent
        // (Domain.Moderation.Events) derives to "moderation.warning.sent".
        ["moderation.warning.sent"] = """
            {
                "moderator_user_id": "424596340",
                "moderator_user_name": "quotrok",
                "user_id": "141981764",
                "user_login": "twitchdev",
                "user_name": "TwitchDev",
                "reason": "cut it out",
                "chat_rules_cited": ["No spam", "Be kind"]
            }
            """,

        // WarningSuspiciousShieldTranslatorsTests.ChannelSuspiciousUserMessage_PublishesEvent_WithNestedMessage
        // Twitch subscription type: channel.suspicious_user.message. Catalog wire name: SuspiciousUserMessageEvent
        // (Domain.Moderation.Events) derives to "moderation.suspicious.user.message".
        ["moderation.suspicious.user.message"] = """
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
            """,

        // WarningSuspiciousShieldTranslatorsTests.ChannelSuspiciousUserUpdate_PublishesEvent_WithModeratorAndStatus
        // Twitch subscription type: channel.suspicious_user.update. Catalog wire name: SuspiciousUserUpdatedEvent
        // (Domain.Moderation.Events) derives to "moderation.suspicious.user.updated".
        ["moderation.suspicious.user.updated"] = """
            {
                "broadcaster_user_id": "1050263435",
                "moderator_user_id": "1050263436",
                "moderator_user_name": "29087e59dfc441968f6",
                "user_id": "1050263437",
                "user_login": "06fbcc75952245c5a87",
                "user_name": "06fbcc75952245c5a87",
                "low_trust_status": "restricted"
            }
            """,

        // WarningSuspiciousShieldTranslatorsTests.ChannelShieldModeBegin_PublishesShieldModeBeganEvent
        // Twitch subscription type: channel.shield_mode.begin. Catalog wire name: ShieldModeBeganEvent
        // (Domain.Moderation.Events) derives to "moderation.shield.mode.began".
        ["moderation.shield.mode.began"] = """
            {
                "broadcaster_user_id": "12345",
                "moderator_user_id": "98765",
                "moderator_user_name": "ParticularlyParticular123",
                "started_at": "2026-06-20T11:00:03Z"
            }
            """,

        // WarningSuspiciousShieldTranslatorsTests.ChannelShieldModeEnd_PublishesShieldModeEndedEvent
        // Twitch subscription type: channel.shield_mode.end. Catalog wire name: ShieldModeEndedEvent
        // (Domain.Moderation.Events) derives to "moderation.shield.mode.ended".
        ["moderation.shield.mode.ended"] = """
            {
                "broadcaster_user_id": "12345",
                "moderator_user_id": "98765",
                "moderator_user_name": "ParticularlyParticular123",
                "ended_at": "2026-06-20T11:30:23Z"
            }
            """,

        // ModerationTranslatorsTests.ChannelBan_PermanentBan_PublishesUserBannedEvent
        // Twitch subscription type: channel.ban (is_permanent branch). Catalog wire name: UserBannedEvent
        // (Domain.Moderation.Events) derives to "moderation.user.banned".
        ["moderation.user.banned"] = """
            {
                "user_id": "1234",
                "user_login": "cool_user",
                "user_name": "Cool_User",
                "broadcaster_user_id": "broadcaster-99",
                "moderator_user_id": "mod-1",
                "moderator_user_name": "Mod_One",
                "reason": "spamming",
                "banned_at": "2026-06-20T11:29:00Z",
                "ends_at": null,
                "is_permanent": true
            }
            """,

        // ModerationTranslatorsTests.ChannelBan_Timeout_PublishesUserTimedOutEvent_WithDerivedDuration
        // Twitch subscription type: channel.ban (ends_at branch). Catalog wire name: UserTimedOutEvent
        // (Domain.Moderation.Events) derives to "moderation.user.timed.out".
        ["moderation.user.timed.out"] = """
            {
                "user_id": "1234",
                "user_login": "cool_user",
                "user_name": "Cool_User",
                "moderator_user_id": "mod-1",
                "reason": "cool down",
                "banned_at": "2026-06-20T11:00:00Z",
                "ends_at": "2026-06-20T11:10:00Z",
                "is_permanent": false
            }
            """,

        // ModerationTranslatorsTests.ChannelUnban_PublishesUserUnbannedEvent
        // Twitch subscription type: channel.unban. Catalog wire name: UserUnbannedEvent
        // (Domain.Moderation.Events) derives to "moderation.user.unbanned".
        ["moderation.user.unbanned"] = """
            {
                "user_id": "1234",
                "user_login": "cool_user",
                "user_name": "Cool_User",
                "moderator_user_id": "mod-1",
                "moderator_user_name": "Mod_One"
            }
            """,

        // ModerationTranslatorsTests.ChannelUnbanRequestCreate_PublishesUnbanRequestCreatedEvent
        // Twitch subscription type: channel.unban_request.create. Catalog wire name: UnbanRequestCreatedEvent
        // (Domain.Moderation.Events) derives to "moderation.unban.request.created".
        ["moderation.unban.request.created"] = """
            {
                "id": "60",
                "user_id": "1339",
                "user_login": "not_cool_user",
                "user_name": "Not_Cool_User",
                "text": "unban me",
                "created_at": "2026-06-20T11:00:00Z"
            }
            """,

        // ModerationTranslatorsTests.ChannelUnbanRequestResolve_PublishesUnbanRequestResolvedEvent
        // Twitch subscription type: channel.unban_request.resolve. Catalog wire name: UnbanRequestResolvedEvent
        // (Domain.Moderation.Events) derives to "moderation.unban.request.resolved".
        ["moderation.unban.request.resolved"] = """
            {
                "id": "60",
                "user_id": "1339",
                "user_login": "not_cool_user",
                "user_name": "Not_Cool_User",
                "moderator_user_id": "1337",
                "moderator_user_name": "Cool_User",
                "resolution_text": "no",
                "status": "denied"
            }
            """,

        // ModerationTranslatorsTests.ChannelModeratorAdd_PublishesModeratorAddedEvent
        // Twitch subscription type: channel.moderator.add. Catalog wire name: ModeratorAddedEvent
        // (Domain.Moderation.Events) derives to "moderation.moderator.added".
        ["moderation.moderator.added"] = """
            {
                "user_id": "141981764",
                "user_login": "twitchdev",
                "user_name": "TwitchDev"
            }
            """,

        // ModerationTranslatorsTests.ChannelModeratorRemove_PublishesModeratorRemovedEvent
        // Twitch subscription type: channel.moderator.remove. Catalog wire name: ModeratorRemovedEvent
        // (Domain.Moderation.Events) derives to "moderation.moderator.removed".
        ["moderation.moderator.removed"] = """
            {
                "user_id": "141981764",
                "user_login": "twitchdev",
                "user_name": "TwitchDev"
            }
            """,

        // ModerationTranslatorsTests.ChannelVipAdd_PublishesVipAddedEvent
        // Twitch subscription type: channel.vip.add. Catalog wire name: VipAddedEvent
        // (Domain.Moderation.Events) derives to "moderation.vip.added".
        ["moderation.vip.added"] = """
            {
                "user_id": "1234",
                "user_login": "mod_user",
                "user_name": "Mod_User"
            }
            """,

        // ModerationTranslatorsTests.ChannelVipRemove_PublishesVipRemovedEvent
        // Twitch subscription type: channel.vip.remove. Catalog wire name: VipRemovedEvent
        // (Domain.Moderation.Events) derives to "moderation.vip.removed".
        ["moderation.vip.removed"] = """
            {
                "user_id": "1234",
                "user_login": "mod_user",
                "user_name": "Mod_User"
            }
            """,

        // ChannelModerateTranslatorTests.ChannelModerate_BanAction_MapsActionModeratorAndTargetWithReason
        // Twitch subscription type: channel.moderate (v2). Catalog wire name: ModerationActionTakenEvent
        // (Domain.Moderation.Events) derives to "moderation.action.taken".
        ["moderation.action.taken"] = """
            {
                "broadcaster_user_id": "423374343",
                "moderator_user_id": "424596340",
                "moderator_user_login": "quotrok",
                "moderator_user_name": "quotrok",
                "action": "ban",
                "followers": null,
                "ban": {
                    "user_id": "141981764",
                    "user_login": "twitchdev",
                    "user_name": "TwitchDev",
                    "reason": "rule violation"
                },
                "timeout": null,
                "delete": null
            }
            """,

        // ChannelModerateTranslatorTests.ChannelModerate_RaidAction_AlsoPublishesTheOutgoingRaidEvent
        // Twitch subscription type: channel.moderate (v2, action "raid"). Catalog wire name: OutgoingRaidEvent
        // (Domain.Stream.Events) derives to "stream.outgoing.raid".
        ["stream.outgoing.raid"] = """
            {
                "broadcaster_user_id": "423374343",
                "moderator_user_id": "423374343",
                "action": "raid",
                "ban": null,
                "raid": {
                    "user_id": "141981764",
                    "user_login": "twitchdev",
                    "user_name": "TwitchDev",
                    "viewer_count": 42
                }
            }
            """,

        // SubscriptionTranslatorsTests.ChannelSubscribe_PublishesNewSubscriptionEvent_WithParsedFields
        // Twitch subscription type: channel.subscribe. Catalog wire name: NewSubscriptionEvent
        // (Domain.Rewards.Events) derives to "rewards.new.subscription".
        ["rewards.new.subscription"] = """
            {
                "user_id": "1234",
                "user_login": "cool_user",
                "user_name": "Cool_User",
                "broadcaster_user_id": "broadcaster-99",
                "tier": "1000",
                "is_gift": false
            }
            """,

        // SubscriptionTranslatorsTests.ChannelSubscriptionMessage_PublishesResubscriptionEvent_WithNestedMessageText
        // Twitch subscription type: channel.subscription.message. Catalog wire name: ResubscriptionEvent
        // (Domain.Rewards.Events) derives to "rewards.resubscription".
        ["rewards.resubscription"] = """
            {
                "user_id": "1234",
                "user_login": "cool_user",
                "user_name": "Cool_User",
                "tier": "1000",
                "cumulative_months": 15,
                "streak_months": 3,
                "duration_months": 6,
                "message": { "text": "Love the stream!", "emotes": [] }
            }
            """,

        // SubscriptionTranslatorsTests.ChannelSubscriptionGift_PublishesGiftSubscriptionEvent_WithGifterAndTotal
        // Twitch subscription type: channel.subscription.gift. Catalog wire name: GiftSubscriptionEvent
        // (Domain.Rewards.Events) derives to "rewards.gift.subscription".
        ["rewards.gift.subscription"] = """
            {
                "user_id": "1234",
                "user_login": "cool_user",
                "user_name": "Cool_User",
                "tier": "1000",
                "total": 5,
                "cumulative_total": 50,
                "is_anonymous": false
            }
            """,

        // SubscriptionTranslatorsTests.ChannelSubscriptionEnd_PublishesSubscriptionEndedEvent_WithParsedFields
        // Twitch subscription type: channel.subscription.end. Catalog wire name: SubscriptionEndedEvent
        // (Domain.Rewards.Events) derives to "rewards.subscription.ended".
        ["rewards.subscription.ended"] = """
            {
                "user_id": "1234",
                "user_login": "cool_user",
                "user_name": "Cool_User",
                "tier": "3000",
                "is_gift": true
            }
            """,

        // SubscriptionTranslatorsTests.ChannelCheer_PublishesCheerEvent_WithParsedFields
        // Twitch subscription type: channel.cheer. Catalog wire name: CheerEvent (Domain.Rewards.Events)
        // derives to "rewards.cheer".
        ["rewards.cheer"] = """
            {
                "is_anonymous": false,
                "user_id": "1234",
                "user_login": "cool_user",
                "user_name": "Cool_User",
                "broadcaster_user_id": "broadcaster-99",
                "message": "cheer100 nice stream",
                "bits": 100
            }
            """,

        // AdBreakBitsUserTranslatorsTests.BitsUse_PublishesBitsUsedEvent_WithCheerTypeAndMessageText
        // Twitch subscription type: channel.bits.use. Catalog wire name: BitsUsedEvent (Domain.Rewards.Events)
        // derives to "rewards.bits.used".
        ["rewards.bits.used"] = """
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
            """,

        // ChannelPointsTranslatorsTests.RedemptionAdd_PublishesRewardRedeemedEvent_WithNestedRewardMapped
        // Twitch subscription type: channel.channel_points_custom_reward_redemption.add. Catalog wire name:
        // RewardRedeemedEvent (Domain.Rewards.Events) derives to "rewards.reward.redeemed".
        ["rewards.reward.redeemed"] = """
            {
                "id": "17fa2df1-ad76-4804-bfa5-a40ef63efe63",
                "broadcaster_user_id": "1337",
                "user_id": "9001",
                "user_login": "cooler_user",
                "user_name": "Cooler_User",
                "user_input": "pogchamp",
                "status": "unfulfilled",
                "reward": {
                    "id": "92af127c-7326-4483-a52b-b0da0be61c01",
                    "title": "title",
                    "cost": 100,
                    "prompt": "reward prompt"
                },
                "redeemed_at": "2020-07-15T17:16:03.17106713Z"
            }
            """,

        // ChannelPointsTranslatorsTests.RedemptionUpdate_PublishesRedemptionUpdatedEvent_WithFulfilledStatus
        // Twitch subscription type: channel.channel_points_custom_reward_redemption.update. Catalog wire name:
        // RewardRedemptionUpdatedEvent (Domain.Rewards.Events) derives to "rewards.reward.redemption.updated".
        ["rewards.reward.redemption.updated"] = """
            {
                "id": "17fa2df1-ad76-4804-bfa5-a40ef63efe63",
                "broadcaster_user_id": "1337",
                "user_id": "9001",
                "user_login": "cooler_user",
                "user_name": "Cooler_User",
                "user_input": "pogchamp",
                "status": "fulfilled",
                "reward": {
                    "id": "92af127c-7326-4483-a52b-b0da0be61c01",
                    "title": "title",
                    "cost": 100,
                    "prompt": "reward prompt"
                },
                "redeemed_at": "2020-07-15T17:16:03.17106713Z"
            }
            """,

        // ChannelPointsTranslatorsTests.RewardAdd_PublishesRewardCreatedEvent_WithTopLevelFields
        // Twitch subscription type: channel.channel_points_custom_reward.add. Catalog wire name:
        // RewardCreatedEvent (Domain.Rewards.Events) derives to "rewards.reward.created".
        ["rewards.reward.created"] = """
            {
                "id": "9001",
                "broadcaster_user_id": "1337",
                "broadcaster_user_login": "cool_user",
                "broadcaster_user_name": "Cool_User",
                "is_enabled": true,
                "is_paused": false,
                "is_in_stock": true,
                "title": "Cool Reward",
                "cost": 100,
                "prompt": "reward prompt"
            }
            """,

        // ChannelPointsTranslatorsTests.RewardUpdate_PublishesRewardUpdatedEvent_WithDisabledFlag
        // Twitch subscription type: channel.channel_points_custom_reward.update. Catalog wire name:
        // RewardUpdatedEvent (Domain.Rewards.Events) derives to "rewards.reward.updated".
        ["rewards.reward.updated"] = """
            {
                "id": "9001",
                "is_enabled": false,
                "title": "Renamed Reward",
                "cost": 250,
                "prompt": "p"
            }
            """,

        // ChannelPointsTranslatorsTests.RewardRemove_PublishesRewardRemovedEvent
        // Twitch subscription type: channel.channel_points_custom_reward.remove. Catalog wire name:
        // RewardRemovedEvent (Domain.Rewards.Events) derives to "rewards.reward.removed".
        ["rewards.reward.removed"] = """
            {
                "id": "9001",
                "broadcaster_user_id": "1337",
                "is_enabled": true,
                "title": "Cool Reward",
                "cost": 100
            }
            """,

        // ChannelPointsTranslatorsTests.AutomaticRedemptionAddV2_PublishesEvent_WithNestedRewardAndMessage
        // Twitch subscription type: channel.channel_points_automatic_reward_redemption.add (v2). Catalog wire
        // name: AutomaticRewardRedeemedEvent (Domain.Rewards.Events) derives to
        // "rewards.automatic.reward.redeemed".
        ["rewards.automatic.reward.redeemed"] = """
            {
                "broadcaster_user_id": "12826",
                "broadcaster_user_name": "Twitch",
                "broadcaster_user_login": "twitch",
                "user_id": "141981764",
                "user_name": "TwitchDev",
                "user_login": "twitchdev",
                "id": "f024099a-e0fe-4339-9a0a-a706fb59f353",
                "reward": {
                    "type": "send_highlighted_message",
                    "channel_points": 100,
                    "emote": null
                },
                "message": {
                    "text": "Hello world! VoHiYo",
                    "fragments": [
                        { "type": "text", "text": "Hello world! ", "emote": null }
                    ]
                },
                "redeemed_at": "2024-08-12T21:14:34.260398045Z"
            }
            """,

        // ChannelPointsTranslatorsTests.CustomPowerUpRedemptionAdd_PublishesEvent_WithNestedPowerUpMapped
        // Twitch subscription type: channel.custom_power_up_redemption.add. Catalog wire name:
        // CustomPowerUpRedeemedEvent (Domain.Rewards.Events) derives to "rewards.custom.power.up.redeemed".
        ["rewards.custom.power.up.redeemed"] = """
            {
                "id": "17fa2df1-ad76-4804-bfa5-a40ef63efe63",
                "broadcaster_user_id": "1337",
                "broadcaster_user_login": "cool_user",
                "broadcaster_user_name": "Cool_User",
                "user_id": "9001",
                "user_login": "cooler_user",
                "user_name": "Cooler_User",
                "user_input": "pogchamp",
                "status": "unfulfilled",
                "custom_power_up": {
                    "id": "92af127c-7326-4483-a52b-b0da0be61c01",
                    "title": "title",
                    "bits": 100,
                    "prompt": "Power-up prompt"
                },
                "redeemed_at": "2026-05-01T17:16:03.17106713Z"
            }
            """,

        // PollPredictionTranslatorsTests.PollBegin_PublishesPollBeganEvent_WithChoicesAndDuration
        // Twitch subscription type: channel.poll.begin. Catalog wire name: PollBeganEvent
        // (Domain.Community.Events) derives to "community.poll.began".
        ["community.poll.began"] = """
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
            """,

        // PollPredictionTranslatorsTests.PollProgress_PublishesPollProgressEvent_WithRunningTallies
        // Twitch subscription type: channel.poll.progress. Catalog wire name: PollProgressEvent
        // (Domain.Community.Events) derives to "community.poll.progress".
        ["community.poll.progress"] = """
            {
                "id": "poll-1",
                "title": "Pineapple on pizza?",
                "choices": [
                    { "id": "c1", "title": "Yes", "channel_points_votes": 25, "votes": 30 },
                    { "id": "c2", "title": "No", "channel_points_votes": 5, "votes": 12 }
                ],
                "ends_at": "2026-06-20T11:32:00Z"
            }
            """,

        // PollPredictionTranslatorsTests.PollEnd_PublishesPollEndedEvent_WithStatusAndWinningChoice
        // Twitch subscription type: channel.poll.end. Catalog wire name: PollEndedEvent
        // (Domain.Community.Events) derives to "community.poll.ended".
        ["community.poll.ended"] = """
            {
                "id": "poll-1",
                "title": "Pineapple on pizza?",
                "status": "completed",
                "choices": [
                    { "id": "c1", "title": "Yes", "channel_points_votes": 25, "votes": 30 },
                    { "id": "c2", "title": "No", "channel_points_votes": 5, "votes": 42 }
                ]
            }
            """,

        // PollPredictionTranslatorsTests.PredictionBegin_PublishesPredictionBeganEvent_WithOutcomesAndWindow
        // Twitch subscription type: channel.prediction.begin. Catalog wire name: PredictionBeganEvent
        // (Domain.Community.Events) derives to "community.prediction.began".
        ["community.prediction.began"] = """
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
            """,

        // PollPredictionTranslatorsTests.PredictionProgress_PublishesPredictionProgressEvent_WithPools
        // Twitch subscription type: channel.prediction.progress. Catalog wire name: PredictionProgressEvent
        // (Domain.Community.Events) derives to "community.prediction.progress".
        ["community.prediction.progress"] = """
            {
                "id": "pred-1",
                "title": "Will we win?",
                "outcomes": [
                    { "id": "o1", "title": "Yes", "color": "blue", "users": 12, "channel_points": 5000 },
                    { "id": "o2", "title": "No", "color": "pink", "users": 3, "channel_points": 800 }
                ],
                "locks_at": "2026-06-20T11:31:30Z"
            }
            """,

        // PollPredictionTranslatorsTests.PredictionLock_PublishesPredictionLockedEvent_WithOutcomes
        // Twitch subscription type: channel.prediction.lock. Catalog wire name: PredictionLockedEvent
        // (Domain.Community.Events) derives to "community.prediction.locked".
        ["community.prediction.locked"] = """
            {
                "id": "pred-1",
                "title": "Will we win?",
                "outcomes": [
                    { "id": "o1", "title": "Yes", "color": "blue", "users": 12, "channel_points": 5000 }
                ]
            }
            """,

        // PollPredictionTranslatorsTests.PredictionEnd_PublishesPredictionEndedEvent_WithStatusAndWinner
        // Twitch subscription type: channel.prediction.end. Catalog wire name: PredictionEndedEvent
        // (Domain.Community.Events) derives to "community.prediction.ended".
        ["community.prediction.ended"] = """
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
            """,

        // HypeTrainGoalCharityTranslatorsTests.HypeTrainBegin_PublishesBeganEvent_WithContributionsAndGoal
        // Twitch subscription type: channel.hype_train.begin (v2). Catalog wire name: HypeTrainBeganEvent
        // (Domain.Community.Events) derives to "community.hype.train.began".
        ["community.hype.train.began"] = """
            {
                "id": "ht-1",
                "broadcaster_user_id": "1337",
                "level": 2,
                "total": 700,
                "progress": 200,
                "goal": 1000,
                "top_contributions": [
                    { "user_id": "u1", "user_login": "alice", "user_name": "Alice", "type": "bits", "total": 500 },
                    { "user_id": "u2", "user_login": "bob", "user_name": "Bob", "type": "subscription", "total": 200 }
                ],
                "started_at": "2026-06-20T11:30:00Z",
                "expires_at": "2026-06-20T11:35:00Z"
            }
            """,

        // HypeTrainGoalCharityTranslatorsTests.HypeTrainProgress_PublishesProgressEvent_WithAdvancingTotals
        // Twitch subscription type: channel.hype_train.progress (v2). Catalog wire name: HypeTrainProgressEvent
        // (Domain.Community.Events) derives to "community.hype.train.progress".
        ["community.hype.train.progress"] = """
            {
                "id": "ht-1",
                "level": 3,
                "total": 1200,
                "progress": 200,
                "goal": 1500,
                "top_contributions": [
                    { "user_id": "u1", "user_login": "alice", "user_name": "Alice", "type": "bits", "total": 900 }
                ],
                "expires_at": "2026-06-20T11:36:00Z"
            }
            """,

        // HypeTrainGoalCharityTranslatorsTests.HypeTrainEnd_PublishesEndedEvent_WithFinalLevelAndContributions
        // Twitch subscription type: channel.hype_train.end (v2). Catalog wire name: HypeTrainEndedEvent
        // (Domain.Community.Events) derives to "community.hype.train.ended".
        ["community.hype.train.ended"] = """
            {
                "id": "ht-1",
                "level": 5,
                "total": 3500,
                "top_contributions": [
                    { "user_id": "u1", "user_login": "alice", "user_name": "Alice", "type": "bits", "total": 2000 }
                ],
                "started_at": "2026-06-20T11:30:00Z",
                "ended_at": "2026-06-20T11:40:00Z"
            }
            """,

        // HypeTrainGoalCharityTranslatorsTests.GoalBegin_PublishesGoalBeganEvent_WithTypeAndAmounts
        // Twitch subscription type: channel.goal.begin. Catalog wire name: GoalBeganEvent
        // (Domain.Community.Events) derives to "community.goal.began".
        ["community.goal.began"] = """
            {
                "id": "goal-1",
                "broadcaster_user_id": "1337",
                "type": "follower",
                "description": "Road to 1k followers",
                "current_amount": 850,
                "target_amount": 1000,
                "started_at": "2026-06-20T11:00:00Z"
            }
            """,

        // HypeTrainGoalCharityTranslatorsTests.GoalProgress_PublishesGoalProgressEvent_WithUpdatedAmount
        // Twitch subscription type: channel.goal.progress. Catalog wire name: GoalProgressEvent
        // (Domain.Community.Events) derives to "community.goal.progress".
        ["community.goal.progress"] = """
            {
                "id": "goal-1",
                "type": "subscription",
                "description": "Sub goal",
                "current_amount": 920,
                "target_amount": 1000,
                "started_at": "2026-06-20T11:00:00Z"
            }
            """,

        // HypeTrainGoalCharityTranslatorsTests.GoalEnd_PublishesGoalEndedEvent_WithAchievementAndEndTime
        // Twitch subscription type: channel.goal.end. Catalog wire name: GoalEndedEvent
        // (Domain.Community.Events) derives to "community.goal.ended".
        ["community.goal.ended"] = """
            {
                "id": "goal-1",
                "type": "follower",
                "description": "Road to 1k followers",
                "is_achieved": true,
                "current_amount": 1000,
                "target_amount": 1000,
                "started_at": "2026-06-20T11:00:00Z",
                "ended_at": "2026-06-20T11:50:00Z"
            }
            """,

        // HypeTrainGoalCharityTranslatorsTests.CharityStart_PublishesStartedEvent_WithRawMoneyAmounts
        // Twitch subscription type: channel.charity_campaign.start. Catalog wire name: CharityCampaignStartedEvent
        // (Domain.Community.Events) derives to "community.charity.campaign.started".
        ["community.charity.campaign.started"] = """
            {
                "id": "camp-1",
                "broadcaster_user_id": "1337",
                "charity_name": "Save the Cats",
                "charity_description": "Helping cats everywhere",
                "charity_logo": "https://abc/logo.png",
                "charity_website": "https://savethecats.example",
                "current_amount": { "value": 150000, "decimal_places": 2, "currency": "USD" },
                "target_amount": { "value": 1500000, "decimal_places": 2, "currency": "USD" },
                "started_at": "2026-06-20T11:00:00Z"
            }
            """,

        // HypeTrainGoalCharityTranslatorsTests.CharityProgress_PublishesProgressEvent_WithUpdatedCurrentAmount
        // Twitch subscription type: channel.charity_campaign.progress. Catalog wire name:
        // CharityCampaignProgressEvent (Domain.Community.Events) derives to "community.charity.campaign.progress".
        ["community.charity.campaign.progress"] = """
            {
                "id": "camp-1",
                "charity_name": "Save the Cats",
                "current_amount": { "value": 260000, "decimal_places": 2, "currency": "USD" },
                "target_amount": { "value": 1500000, "decimal_places": 2, "currency": "USD" }
            }
            """,

        // HypeTrainGoalCharityTranslatorsTests.CharityDonate_PublishesDonationEvent_WithDonorAndRawAmount
        // Twitch subscription type: channel.charity_campaign.donate. Catalog wire name: CharityDonationEvent
        // (Domain.Community.Events) derives to "community.charity.donation".
        ["community.charity.donation"] = """
            {
                "id": "donation-9",
                "campaign_id": "camp-1",
                "broadcaster_user_id": "1337",
                "user_id": "u7",
                "user_login": "generous_gary",
                "user_name": "Generous_Gary",
                "charity_name": "Save the Cats",
                "amount": { "value": 5000, "decimal_places": 2, "currency": "EUR" }
            }
            """,

        // HypeTrainGoalCharityTranslatorsTests.CharityStop_PublishesStoppedEvent_WithFinalAmountAndStopTime
        // Twitch subscription type: channel.charity_campaign.stop. Catalog wire name: CharityCampaignStoppedEvent
        // (Domain.Community.Events) derives to "community.charity.campaign.stopped".
        ["community.charity.campaign.stopped"] = """
            {
                "id": "camp-1",
                "charity_name": "Save the Cats",
                "current_amount": { "value": 1500000, "decimal_places": 2, "currency": "USD" },
                "target_amount": { "value": 1500000, "decimal_places": 2, "currency": "USD" },
                "stopped_at": "2026-06-20T12:00:00Z"
            }
            """,

        // StreamLifecycleTranslatorsTests.ChannelRaid_PublishesRaidEvent_FromIncomingRaiderFields
        // Twitch subscription type: channel.raid. Catalog wire name: RaidEvent (Domain.Stream.Events)
        // derives to "stream.raid".
        ["stream.raid"] = """
            {
                "from_broadcaster_user_id": "5678",
                "from_broadcaster_user_login": "raiding_streamer",
                "from_broadcaster_user_name": "Raiding_Streamer",
                "to_broadcaster_user_id": "broadcaster-99",
                "to_broadcaster_user_login": "streamer",
                "to_broadcaster_user_name": "Streamer",
                "viewers": 250
            }
            """,

        // StreamLifecycleTranslatorsTests.ChannelUpdate_PublishesChannelUpdatedEvent_WithTitleAndCategory
        // Twitch subscription type: channel.update. Catalog wire name: ChannelUpdatedEvent
        // (Domain.Stream.Events) derives to "stream.channel.updated".
        ["stream.channel.updated"] = """
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
            """,

        // StreamLifecycleTranslatorsTests.StreamOnline_PublishesChannelOnlineEvent_WithParsedStartedAt
        // Twitch subscription type: stream.online. Catalog wire name: ChannelOnlineEvent (Domain.Stream.Events)
        // carries an [Event("stream.online", …)] override.
        ["stream.online"] = """
            {
                "id": "9001",
                "broadcaster_user_id": "broadcaster-99",
                "broadcaster_user_login": "streamer",
                "broadcaster_user_name": "Streamer",
                "type": "live",
                "started_at": "2026-06-20T11:25:00Z"
            }
            """,

        // StreamLifecycleTranslatorsTests.StreamOffline_PublishesChannelOfflineEvent_WithBroadcasterAndZeroDuration
        // Twitch subscription type: stream.offline. Catalog wire name: ChannelOfflineEvent (Domain.Stream.Events)
        // carries an [Event("stream.offline", …)] override.
        ["stream.offline"] = """
            {
                "broadcaster_user_id": "broadcaster-99",
                "broadcaster_user_login": "streamer",
                "broadcaster_user_name": "Streamer"
            }
            """,

        // AdBreakBitsUserTranslatorsTests.AdBreakBegin_PublishesAdBreakBeganEvent_WithNumericDurationAndAutomaticFlag
        // Twitch subscription type: channel.ad_break.begin. Catalog wire name: AdBreakBeganEvent
        // (Domain.Stream.Events) derives to "stream.ad.break.began".
        ["stream.ad.break.began"] = """
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
            """,

        // ShoutoutTranslatorsTests.ShoutoutCreate_PublishesSentEvent_WithTargetBroadcaster
        // Twitch subscription type: channel.shoutout.create. Catalog wire name: ShoutoutSentEvent
        // (Domain.Stream.Events) derives to "stream.shoutout.sent".
        ["stream.shoutout.sent"] = """
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
            """,

        // ShoutoutTranslatorsTests.ShoutoutReceive_PublishesReceivedEvent_WithSourceAndViewerCount
        // Twitch subscription type: channel.shoutout.receive. Catalog wire name: ShoutoutReceivedEvent
        // (Domain.Stream.Events) derives to "stream.shoutout.received".
        ["stream.shoutout.received"] = """
            {
                "broadcaster_user_id": "broadcaster-99",
                "from_broadcaster_user_id": "12345",
                "from_broadcaster_user_login": "big_streamer",
                "from_broadcaster_user_name": "Big_Streamer",
                "viewer_count": 3500,
                "started_at": "2026-06-20T11:29:00Z"
            }
            """,

        // GuestStarTranslatorsTests.GuestStarSessionBegin_PublishesBeganEvent_WithSessionAndStartedAt
        // Twitch subscription type: channel.guest_star_session.begin (beta). Catalog wire name:
        // GuestStarSessionBeganEvent (Domain.Stream.Events) derives to "stream.guest.star.session.began".
        ["stream.guest.star.session.began"] = """
            {
                "broadcaster_user_id": "broadcaster-99",
                "broadcaster_user_login": "streamer",
                "broadcaster_user_name": "Streamer",
                "session_id": "session-2KFRQbFtpmfyD3IevNRnCzOzhg1",
                "started_at": "2026-06-20T11:28:00Z"
            }
            """,

        // GuestStarTranslatorsTests.GuestStarSessionEnd_PublishesEndedEvent_WithStartAndEndTimestamps
        // Twitch subscription type: channel.guest_star_session.end (beta). Catalog wire name:
        // GuestStarSessionEndedEvent (Domain.Stream.Events) derives to "stream.guest.star.session.ended".
        ["stream.guest.star.session.ended"] = """
            {
                "broadcaster_user_id": "broadcaster-99",
                "session_id": "session-abc",
                "started_at": "2026-06-20T11:28:00Z",
                "ended_at": "2026-06-20T11:55:00Z"
            }
            """,

        // GuestStarTranslatorsTests.GuestStarGuestUpdate_PublishesUpdatedEvent_WithGuestStateAndSlot
        // Twitch subscription type: channel.guest_star_guest.update (beta). Catalog wire name:
        // GuestStarGuestUpdatedEvent (Domain.Stream.Events) derives to "stream.guest.star.guest.updated".
        ["stream.guest.star.guest.updated"] = """
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
            """,

        // GuestStarTranslatorsTests.GuestStarSettingsUpdate_PublishesUpdatedEvent_WithFlagsAndLayout
        // Twitch subscription type: channel.guest_star_settings.update (beta). Catalog wire name:
        // GuestStarSettingsUpdatedEvent (Domain.Stream.Events) derives to "stream.guest.star.settings.updated".
        ["stream.guest.star.settings.updated"] = """
            {
                "broadcaster_user_id": "broadcaster-99",
                "is_moderator_send_live_enabled": true,
                "slot_count": 5,
                "is_browser_source_audio_enabled": false,
                "group_layout": "tiled"
            }
            """,

        // AdBreakBitsUserTranslatorsTests.UserUpdate_PublishesUserUpdatedEvent_WithEmailWhenScopeGranted
        // Twitch subscription type: user.update. Catalog wire name: UserUpdatedEvent (Domain.Identity.Events)
        // derives to "identity.user.updated".
        ["identity.user.updated"] = """
            {
                "user_id": "9001",
                "user_login": "the_user",
                "user_name": "The_User",
                "email": "user@example.com",
                "email_verified": true,
                "description": "Just a streamer."
            }
            """,
    };
}

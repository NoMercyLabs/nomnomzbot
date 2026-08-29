// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json.Nodes;
using FluentAssertions;
using NomNomzBot.Application.DevPlatform;
using NomNomzBot.Application.DevPlatform.Dtos;
using NomNomzBot.Infrastructure.DevPlatform;

namespace NomNomzBot.Infrastructure.Tests.DevPlatform;

/// <summary>
/// Proves the SDK event catalog's <see cref="EventCatalogItemDto.SamplePayloadJson"/> is the SAME real fixture
/// the corresponding translator test proves against — not an approximation authored from memory. For every
/// verified event, this parses both the catalog's sample and the translator test's own raw-string fixture as
/// JSON and asserts they carry the identical top-level key set, which only holds if the catalog value was
/// literally copied from that fixture. Every other handleable event must carry no sample at all, so a future
/// fabricated payload cannot slip in unnoticed.
/// </summary>
public sealed class EventSamplePayloadsTests
{
    private static SdkTypeEmitter RealEmitter() => new(new EventCatalog());

    private static ISet<string> TopLevelKeys(string json) =>
        ((JsonObject)JsonNode.Parse(json)!).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);

    [Theory]
    // wire name in the SDK catalog -> the exact fixture literal from the translator's own behaviour test.
    [InlineData(
        "community.follow",
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
    )]
    [InlineData(
        "chat.message",
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
    )]
    [InlineData(
        "chat.message.deleted",
        """
            {
                "broadcaster_user_id": "broadcaster-99",
                "target_user_id": "321",
                "target_user_name": "Naughty",
                "target_user_login": "naughty",
                "message_id": "del-1"
            }
            """
    )]
    [InlineData(
        "chat.cleared",
        """
            { "broadcaster_user_id": "broadcaster-99", "broadcaster_user_login": "streamer", "broadcaster_user_name": "Streamer" }
            """
    )]
    [InlineData(
        "chat.user.messages.cleared",
        """
            {
                "broadcaster_user_id": "broadcaster-99",
                "target_user_id": "777",
                "target_user_name": "Spammer",
                "target_user_login": "spammer"
            }
            """
    )]
    [InlineData(
        "chat.notification",
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
    )]
    [InlineData(
        "chat.settings.updated",
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
    )]
    [InlineData(
        "chat.user.message.held",
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
    )]
    [InlineData(
        "chat.user.message.updated",
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
    )]
    [InlineData(
        "rewards.watch.streak.received",
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
    )]
    [InlineData(
        "chat.shared.chat.began",
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
    )]
    [InlineData(
        "chat.shared.chat.updated",
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
    )]
    [InlineData(
        "chat.shared.chat.ended",
        """
            {
                "session_id": "session-abc",
                "broadcaster_user_id": "broadcaster-99",
                "host_broadcaster_user_id": "host-1",
                "host_broadcaster_user_login": "host_streamer",
                "host_broadcaster_user_name": "Host_Streamer"
            }
            """
    )]
    [InlineData(
        "chat.whisper.received",
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
    )]
    [InlineData(
        "moderation.auto.mod.message.held",
        """
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
            """
    )]
    [InlineData(
        "moderation.auto.mod.message.updated",
        """
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
            """
    )]
    [InlineData(
        "moderation.auto.mod.settings.updated",
        """
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
            """
    )]
    [InlineData(
        "moderation.auto.mod.terms.updated",
        """
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
            """
    )]
    [InlineData(
        "moderation.warning.acknowledged",
        """
            {
                "user_id": "141981764",
                "user_login": "twitchdev",
                "user_name": "TwitchDev"
            }
            """
    )]
    [InlineData(
        "moderation.warning.sent",
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
    )]
    [InlineData(
        "moderation.suspicious.user.message",
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
    )]
    [InlineData(
        "moderation.suspicious.user.updated",
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
    )]
    [InlineData(
        "moderation.shield.mode.began",
        """
            {
                "broadcaster_user_id": "12345",
                "moderator_user_id": "98765",
                "moderator_user_name": "ParticularlyParticular123",
                "started_at": "2026-06-20T11:00:03Z"
            }
            """
    )]
    [InlineData(
        "moderation.shield.mode.ended",
        """
            {
                "broadcaster_user_id": "12345",
                "moderator_user_id": "98765",
                "moderator_user_name": "ParticularlyParticular123",
                "ended_at": "2026-06-20T11:30:23Z"
            }
            """
    )]
    [InlineData(
        "moderation.user.banned",
        """
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
            """
    )]
    [InlineData(
        "moderation.user.timed.out",
        """
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
            """
    )]
    [InlineData(
        "moderation.user.unbanned",
        """
            {
                "user_id": "1234",
                "user_login": "cool_user",
                "user_name": "Cool_User",
                "moderator_user_id": "mod-1",
                "moderator_user_name": "Mod_One"
            }
            """
    )]
    [InlineData(
        "moderation.unban.request.created",
        """
            {
                "id": "60",
                "user_id": "1339",
                "user_login": "not_cool_user",
                "user_name": "Not_Cool_User",
                "text": "unban me",
                "created_at": "2026-06-20T11:00:00Z"
            }
            """
    )]
    [InlineData(
        "moderation.unban.request.resolved",
        """
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
            """
    )]
    [InlineData(
        "moderation.moderator.added",
        """
            {
                "user_id": "141981764",
                "user_login": "twitchdev",
                "user_name": "TwitchDev"
            }
            """
    )]
    [InlineData(
        "moderation.moderator.removed",
        """
            {
                "user_id": "141981764",
                "user_login": "twitchdev",
                "user_name": "TwitchDev"
            }
            """
    )]
    [InlineData(
        "moderation.vip.added",
        """
            {
                "user_id": "1234",
                "user_login": "mod_user",
                "user_name": "Mod_User"
            }
            """
    )]
    [InlineData(
        "moderation.vip.removed",
        """
            {
                "user_id": "1234",
                "user_login": "mod_user",
                "user_name": "Mod_User"
            }
            """
    )]
    [InlineData(
        "moderation.action.taken",
        """
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
            """
    )]
    [InlineData(
        "stream.outgoing.raid",
        """
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
            """
    )]
    [InlineData(
        "rewards.new.subscription",
        """
            {
                "user_id": "1234",
                "user_login": "cool_user",
                "user_name": "Cool_User",
                "broadcaster_user_id": "broadcaster-99",
                "tier": "1000",
                "is_gift": false
            }
            """
    )]
    [InlineData(
        "rewards.resubscription",
        """
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
            """
    )]
    [InlineData(
        "rewards.gift.subscription",
        """
            {
                "user_id": "1234",
                "user_login": "cool_user",
                "user_name": "Cool_User",
                "tier": "1000",
                "total": 5,
                "cumulative_total": 50,
                "is_anonymous": false
            }
            """
    )]
    [InlineData(
        "rewards.subscription.ended",
        """
            {
                "user_id": "1234",
                "user_login": "cool_user",
                "user_name": "Cool_User",
                "tier": "3000",
                "is_gift": true
            }
            """
    )]
    [InlineData(
        "rewards.cheer",
        """
            {
                "is_anonymous": false,
                "user_id": "1234",
                "user_login": "cool_user",
                "user_name": "Cool_User",
                "broadcaster_user_id": "broadcaster-99",
                "message": "cheer100 nice stream",
                "bits": 100
            }
            """
    )]
    [InlineData(
        "rewards.bits.used",
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
    )]
    [InlineData(
        "rewards.reward.redeemed",
        """
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
            """
    )]
    [InlineData(
        "rewards.reward.redemption.updated",
        """
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
            """
    )]
    [InlineData(
        "rewards.reward.created",
        """
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
            """
    )]
    [InlineData(
        "rewards.reward.updated",
        """
            {
                "id": "9001",
                "is_enabled": false,
                "title": "Renamed Reward",
                "cost": 250,
                "prompt": "p"
            }
            """
    )]
    [InlineData(
        "rewards.reward.removed",
        """
            {
                "id": "9001",
                "broadcaster_user_id": "1337",
                "is_enabled": true,
                "title": "Cool Reward",
                "cost": 100
            }
            """
    )]
    [InlineData(
        "rewards.automatic.reward.redeemed",
        """
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
            """
    )]
    [InlineData(
        "rewards.custom.power.up.redeemed",
        """
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
            """
    )]
    [InlineData(
        "community.poll.began",
        """
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
            """
    )]
    [InlineData(
        "community.poll.progress",
        """
            {
                "id": "poll-1",
                "title": "Pineapple on pizza?",
                "choices": [
                    { "id": "c1", "title": "Yes", "channel_points_votes": 25, "votes": 30 },
                    { "id": "c2", "title": "No", "channel_points_votes": 5, "votes": 12 }
                ],
                "ends_at": "2026-06-20T11:32:00Z"
            }
            """
    )]
    [InlineData(
        "community.poll.ended",
        """
            {
                "id": "poll-1",
                "title": "Pineapple on pizza?",
                "status": "completed",
                "choices": [
                    { "id": "c1", "title": "Yes", "channel_points_votes": 25, "votes": 30 },
                    { "id": "c2", "title": "No", "channel_points_votes": 5, "votes": 42 }
                ]
            }
            """
    )]
    [InlineData(
        "community.prediction.began",
        """
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
            """
    )]
    [InlineData(
        "community.prediction.progress",
        """
            {
                "id": "pred-1",
                "title": "Will we win?",
                "outcomes": [
                    { "id": "o1", "title": "Yes", "color": "blue", "users": 12, "channel_points": 5000 },
                    { "id": "o2", "title": "No", "color": "pink", "users": 3, "channel_points": 800 }
                ],
                "locks_at": "2026-06-20T11:31:30Z"
            }
            """
    )]
    [InlineData(
        "community.prediction.locked",
        """
            {
                "id": "pred-1",
                "title": "Will we win?",
                "outcomes": [
                    { "id": "o1", "title": "Yes", "color": "blue", "users": 12, "channel_points": 5000 }
                ]
            }
            """
    )]
    [InlineData(
        "community.prediction.ended",
        """
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
            """
    )]
    [InlineData(
        "community.hype.train.began",
        """
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
            """
    )]
    [InlineData(
        "community.hype.train.progress",
        """
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
            """
    )]
    [InlineData(
        "community.hype.train.ended",
        """
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
            """
    )]
    [InlineData(
        "community.goal.began",
        """
            {
                "id": "goal-1",
                "broadcaster_user_id": "1337",
                "type": "follower",
                "description": "Road to 1k followers",
                "current_amount": 850,
                "target_amount": 1000,
                "started_at": "2026-06-20T11:00:00Z"
            }
            """
    )]
    [InlineData(
        "community.goal.progress",
        """
            {
                "id": "goal-1",
                "type": "subscription",
                "description": "Sub goal",
                "current_amount": 920,
                "target_amount": 1000,
                "started_at": "2026-06-20T11:00:00Z"
            }
            """
    )]
    [InlineData(
        "community.goal.ended",
        """
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
            """
    )]
    [InlineData(
        "community.charity.campaign.started",
        """
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
            """
    )]
    [InlineData(
        "community.charity.campaign.progress",
        """
            {
                "id": "camp-1",
                "charity_name": "Save the Cats",
                "current_amount": { "value": 260000, "decimal_places": 2, "currency": "USD" },
                "target_amount": { "value": 1500000, "decimal_places": 2, "currency": "USD" }
            }
            """
    )]
    [InlineData(
        "community.charity.donation",
        """
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
            """
    )]
    [InlineData(
        "community.charity.campaign.stopped",
        """
            {
                "id": "camp-1",
                "charity_name": "Save the Cats",
                "current_amount": { "value": 1500000, "decimal_places": 2, "currency": "USD" },
                "target_amount": { "value": 1500000, "decimal_places": 2, "currency": "USD" },
                "stopped_at": "2026-06-20T12:00:00Z"
            }
            """
    )]
    [InlineData(
        "stream.raid",
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
    )]
    [InlineData(
        "stream.channel.updated",
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
    )]
    [InlineData(
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
    )]
    [InlineData(
        "stream.offline",
        """
            {
                "broadcaster_user_id": "broadcaster-99",
                "broadcaster_user_login": "streamer",
                "broadcaster_user_name": "Streamer"
            }
            """
    )]
    [InlineData(
        "stream.ad.break.began",
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
    )]
    [InlineData(
        "stream.shoutout.sent",
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
    )]
    [InlineData(
        "stream.shoutout.received",
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
    )]
    [InlineData(
        "stream.guest.star.session.began",
        """
            {
                "broadcaster_user_id": "broadcaster-99",
                "broadcaster_user_login": "streamer",
                "broadcaster_user_name": "Streamer",
                "session_id": "session-2KFRQbFtpmfyD3IevNRnCzOzhg1",
                "started_at": "2026-06-20T11:28:00Z"
            }
            """
    )]
    [InlineData(
        "stream.guest.star.session.ended",
        """
            {
                "broadcaster_user_id": "broadcaster-99",
                "session_id": "session-abc",
                "started_at": "2026-06-20T11:28:00Z",
                "ended_at": "2026-06-20T11:55:00Z"
            }
            """
    )]
    [InlineData(
        "stream.guest.star.guest.updated",
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
    )]
    [InlineData(
        "stream.guest.star.settings.updated",
        """
            {
                "broadcaster_user_id": "broadcaster-99",
                "is_moderator_send_live_enabled": true,
                "slot_count": 5,
                "is_browser_source_audio_enabled": false,
                "group_layout": "tiled"
            }
            """
    )]
    [InlineData(
        "identity.user.updated",
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
    )]
    public void Catalog_sample_payload_matches_the_translator_tests_own_fixture_shape(
        string wireName,
        string translatorTestFixtureJson
    )
    {
        EventCatalogItemDto item = RealEmitter()
            .EmitEventCatalog(SdkContext.Script)
            .Single(c => c.WireName == wireName);

        item.SamplePayloadJson.Should().NotBeNull($"'{wireName}' has a verified real fixture");
        TopLevelKeys(item.SamplePayloadJson!)
            .Should()
            .BeEquivalentTo(
                TopLevelKeys(translatorTestFixtureJson),
                "the catalog sample must be the same real fixture the translator test proves against"
            );
    }

    [Fact]
    public void Events_with_no_verified_fixture_carry_no_fabricated_sample()
    {
        IReadOnlyList<EventCatalogItemDto> catalog = RealEmitter()
            .EmitEventCatalog(SdkContext.Script);

        // commands.command.executed has no translator-test fixture pinned in EventSamplePayloads (there is no
        // EventSub topic for it — it is raised by the pipeline engine itself) — it must stay null, never a
        // made-up payload standing in for a real one.
        catalog
            .Single(c => c.WireName == "commands.command.executed")
            .SamplePayloadJson.Should()
            .BeNull();
    }
}

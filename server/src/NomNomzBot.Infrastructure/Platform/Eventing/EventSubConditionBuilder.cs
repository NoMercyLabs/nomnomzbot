// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// The per-topic create facts for EventSub subscriptions (twitch-eventsub §3.3): the Twitch <c>condition</c>
/// shape, the topic <c>version</c>, and which token authorizes the create. One home for this knowledge so the
/// transport and service stay generic across the full subscription-type surface.
/// <para>
/// Condition shape is classified, not enumerated per type: most topics key on <c>broadcaster_user_id</c> alone;
/// moderator-plane topics (follow v2, chat moderation, automod, shield/shoutout, warnings, suspicious users,
/// unban requests, guest star) additionally carry <c>moderator_user_id</c>; chat-read topics carry
/// <c>user_id</c> (the reading identity); raids key on <c>to_broadcaster_user_id</c>; user-plane topics
/// (<c>user.update</c>, <c>user.whisper.message</c>) key on <c>user_id</c> only. The broadcaster moderates and
/// reads its own channel, so every moderator/user id resolves to the same tenant Twitch id (a dedicated
/// bot-moderator identity is an additive enhancement, not a shape change).
/// </para>
/// </summary>
public sealed class EventSubConditionBuilder : IEventSubConditionBuilder
{
    private enum ConditionShape
    {
        BroadcasterOnly,
        BroadcasterAndModerator,
        BroadcasterAndUser,
        RaidTo,
        UserOnly,
    }

    public IReadOnlyDictionary<string, string> BuildCondition(
        string eventType,
        string twitchBroadcasterUserId
    ) =>
        ShapeOf(eventType) switch
        {
            ConditionShape.BroadcasterAndModerator => new Dictionary<string, string>
            {
                ["broadcaster_user_id"] = twitchBroadcasterUserId,
                ["moderator_user_id"] = twitchBroadcasterUserId,
            },
            ConditionShape.BroadcasterAndUser => new Dictionary<string, string>
            {
                ["broadcaster_user_id"] = twitchBroadcasterUserId,
                ["user_id"] = twitchBroadcasterUserId,
            },
            ConditionShape.RaidTo => new Dictionary<string, string>
            {
                ["to_broadcaster_user_id"] = twitchBroadcasterUserId,
            },
            ConditionShape.UserOnly => new Dictionary<string, string>
            {
                ["user_id"] = twitchBroadcasterUserId,
            },
            _ => new Dictionary<string, string>
            {
                ["broadcaster_user_id"] = twitchBroadcasterUserId,
            },
        };

    public string GetVersion(string eventType) =>
        eventType switch
        {
            "channel.follow" => "2",
            "channel.update" => "2",
            "channel.moderate" => "2",
            "channel.hype_train.begin" => "2",
            "channel.hype_train.progress" => "2",
            "channel.hype_train.end" => "2",
            "automod.message.hold" => "2",
            "automod.message.update" => "2",
            "channel.channel_points_automatic_reward_redemption.add" => "2",
            "channel.guest_star_session.begin" => "beta",
            "channel.guest_star_session.end" => "beta",
            "channel.guest_star_guest.update" => "beta",
            "channel.guest_star_settings.update" => "beta",
            _ => "1",
        };

    // Only the explicitly auth-free topics ride the app/bot token; everything else needs the broadcaster's user
    // token (the safe default — a new broadcaster-scoped topic does not silently fall through to the app token).
    public bool RequiresBroadcasterToken(string eventType) => !AppTokenEvents.Contains(eventType);

    private static ConditionShape ShapeOf(string eventType) =>
        eventType switch
        {
            "channel.raid" => ConditionShape.RaidTo,
            "user.update" or "user.whisper.message" => ConditionShape.UserOnly,
            _ when ModeratorPlaneEvents.Contains(eventType) =>
                ConditionShape.BroadcasterAndModerator,
            _ when ChatReadEvents.Contains(eventType) => ConditionShape.BroadcasterAndUser,
            _ => ConditionShape.BroadcasterOnly,
        };

    /// <summary>Topics whose condition carries <c>moderator_user_id</c> alongside the broadcaster.</summary>
    private static readonly HashSet<string> ModeratorPlaneEvents =
    [
        "channel.follow",
        "channel.shoutout.create",
        "channel.shoutout.receive",
        "channel.shield_mode.begin",
        "channel.shield_mode.end",
        "channel.moderate",
        "channel.unban_request.create",
        "channel.unban_request.resolve",
        "channel.suspicious_user.message",
        "channel.suspicious_user.update",
        "channel.warning.acknowledge",
        "channel.warning.send",
        "automod.message.hold",
        "automod.message.update",
        "automod.settings.update",
        "automod.terms.update",
        "channel.guest_star_session.begin",
        "channel.guest_star_session.end",
        "channel.guest_star_guest.update",
        "channel.guest_star_settings.update",
    ];

    /// <summary>Chat-read topics whose condition carries the reading <c>user_id</c> alongside the broadcaster.</summary>
    private static readonly HashSet<string> ChatReadEvents =
    [
        "channel.chat.message",
        "channel.chat.notification",
        "channel.chat.message_delete",
        "channel.chat.clear",
        "channel.chat.clear_user_messages",
        "channel.chat_settings.update",
        "channel.chat.user_message_hold",
        "channel.chat.user_message_update",
    ];

    /// <summary>Topics Twitch documents as requiring no authorization — they ride the app/bot token.</summary>
    private static readonly HashSet<string> AppTokenEvents =
    [
        "channel.update",
        "channel.raid",
        "stream.online",
        "stream.offline",
    ];
}

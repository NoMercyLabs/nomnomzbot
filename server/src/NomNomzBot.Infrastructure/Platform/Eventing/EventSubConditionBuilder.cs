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
/// (<c>user.update</c>, <c>user.whisper.message</c>) key on <c>user_id</c> only.
/// </para>
/// <para>
/// Token ownership: a topic is created with the token of the identity that AUTHORIZES it. Chat-read topics
/// (and the bot's own whisper inbox) are read under the bot's <c>user:read:chat</c> identity → the bot's token,
/// with the bot's id in the <c>user_id</c> slot. Every other topic is authorized by the BROADCASTER (their
/// <c>channel:*</c> / <c>moderator:*</c> scopes) → the broadcaster's own token, with the broadcaster's id in any
/// <c>moderator_user_id</c> / <c>user_id</c> slot (the broadcaster is implicitly a moderator of their own
/// channel). Creating each channel's subscriptions with that channel's own token is what makes the scoped topics
/// authorize at all (the bot token holds only chat scopes) AND keeps them within Twitch's per-user-token cost
/// budget (each broadcaster authorized the app, so <c>stream.online</c>/<c>channel.update</c> and the
/// user-authorization topics are cost-0 on the broadcaster's own budget instead of piling onto the bot's single
/// <c>max_total_cost</c> of 10). When no dedicated bot account exists (the streamer IS the bot), the broadcaster
/// id fills the bot slot too — valid for a single-account self-host.
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
        string twitchBroadcasterUserId,
        string? botTwitchUserId = null
    )
    {
        // The user_id / moderator_user_id slot is filled by the identity whose token authorizes the create:
        //  - bot-owned topics (chat-read + the bot's own whisper inbox) ride the bot's user token → the bot's
        //    Twitch id. When no dedicated bot account is configured (the streamer IS the bot), fall back to the
        //    broadcaster id (single-account self-host).
        //  - every other topic rides the BROADCASTER's own token (it holds the channel/moderator scopes and gets
        //    its own per-token cost budget), so the broadcaster fills the moderator_user_id / user_id slot too
        //    (the broadcaster is implicitly a moderator of their own channel).
        string slotUserId = IsBotOwnedTopic(eventType)
            ? botTwitchUserId ?? twitchBroadcasterUserId
            : twitchBroadcasterUserId;
        return ShapeOf(eventType) switch
        {
            ConditionShape.BroadcasterAndModerator => new Dictionary<string, string>
            {
                ["broadcaster_user_id"] = twitchBroadcasterUserId,
                ["moderator_user_id"] = slotUserId,
            },
            ConditionShape.BroadcasterAndUser => new Dictionary<string, string>
            {
                ["broadcaster_user_id"] = twitchBroadcasterUserId,
                ["user_id"] = slotUserId,
            },
            ConditionShape.RaidTo => new Dictionary<string, string>
            {
                ["to_broadcaster_user_id"] = twitchBroadcasterUserId,
            },
            ConditionShape.UserOnly => new Dictionary<string, string> { ["user_id"] = slotUserId },
            _ => new Dictionary<string, string>
            {
                ["broadcaster_user_id"] = twitchBroadcasterUserId,
            },
        };
    }

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

    // A topic rides the BROADCASTER's user token unless it is bot-owned (chat-read + the bot's own whispers,
    // which the bot subscribes under its own user:read:chat identity). The broadcaster holds every channel /
    // moderator scope the scoped topics need — the bot token holds only chat scopes, so riding the bot token is
    // exactly why the scoped topics 403'd — and each broadcaster gets its own per-token cost budget, so the
    // cost-1 topics no longer pile onto the bot's single max_total_cost of 10 (they went 429 "deferred").
    public bool RequiresBroadcasterToken(string eventType) => !IsBotOwnedTopic(eventType);

    // Topics the BOT subscribes under its own user token because it is the reading identity: the chat-read set
    // (user:read:chat) and its own whisper inbox. Every other topic is authorized by the broadcaster.
    private static bool IsBotOwnedTopic(string eventType) =>
        ChatReadEvents.Contains(eventType) || eventType == "user.whisper.message";

    /// <summary>
    /// True for cost-0 EventSub topics — the chat-read set the bot subscribes with its own authorized
    /// <c>user:read:chat</c> identity. Twitch charges 0 for a subscription the target user authorized, so these
    /// never count against the WebSocket <c>max_total_cost</c> cap (10); they must be subscribed FIRST so chat
    /// lands even when the cost-1 topics have exhausted the budget. Drives re-register / sync ordering.
    /// </summary>
    internal static bool IsCost0Topic(string eventType) => ChatReadEvents.Contains(eventType);

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
}

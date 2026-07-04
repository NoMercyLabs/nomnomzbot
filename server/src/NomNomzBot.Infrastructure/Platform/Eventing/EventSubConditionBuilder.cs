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
/// unban requests) additionally carry <c>moderator_user_id</c>; chat-read topics carry <c>user_id</c> (the
/// reading identity); raids key on <c>to_broadcaster_user_id</c>; user-plane topics (<c>user.update</c>,
/// <c>user.whisper.message</c>) key on <c>user_id</c> only.
/// <para>
/// Guest Star is intentionally absent from this surface: Twitch has deprecated the Guest Star API, so its
/// EventSub topics (<c>channel.guest_star_*</c>, all <c>beta</c>) are never subscribed — see
/// <c>twitch-eventsub.md</c> for the decision.
/// </para>
/// </para>
/// <para>
/// Multi-tenant WebSocket constraint: Twitch requires all subscription POSTs for a given WebSocket session to
/// come from the same Twitch user. We satisfy this by always using the bot's user token regardless of which
/// channel is being subscribed. When a dedicated bot account is configured, its Twitch user id replaces the
/// broadcaster id in the <c>user_id</c> / <c>moderator_user_id</c> slots. When no bot account exists (the
/// streamer IS the bot), the broadcaster id fills both slots — valid for a single-channel self-host.
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
        // For user_id / moderator_user_id slots: prefer the bot's Twitch id so that ALL subscription
        // creates for this WebSocket session come from the same Twitch user (the bot). When no dedicated
        // bot account is configured, fall back to the broadcaster id (single-user self-host).
        string botOrBroadcaster = botTwitchUserId ?? twitchBroadcasterUserId;
        return ShapeOf(eventType) switch
        {
            ConditionShape.BroadcasterAndModerator => new Dictionary<string, string>
            {
                ["broadcaster_user_id"] = twitchBroadcasterUserId,
                ["moderator_user_id"] = botOrBroadcaster,
            },
            ConditionShape.BroadcasterAndUser => new Dictionary<string, string>
            {
                ["broadcaster_user_id"] = twitchBroadcasterUserId,
                ["user_id"] = botOrBroadcaster,
            },
            ConditionShape.RaidTo => new Dictionary<string, string>
            {
                ["to_broadcaster_user_id"] = twitchBroadcasterUserId,
            },
            ConditionShape.UserOnly => new Dictionary<string, string>
            {
                ["user_id"] = botOrBroadcaster,
            },
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
            _ => "1",
        };

    // All subscriptions ride the bot/app token regardless of topic. Multi-tenant WebSocket EventSub requires
    // every subscription POST to come from the same Twitch user; the bot is that user. Broadcaster-token
    // subscriptions worked fine for a single channel but break the moment a second channel is added because
    // Twitch rejects "subscriptions created by different users" on the same session.
    public bool RequiresBroadcasterToken(string eventType) => false;

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

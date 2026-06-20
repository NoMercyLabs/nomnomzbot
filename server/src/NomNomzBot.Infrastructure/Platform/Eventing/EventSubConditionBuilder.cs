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
/// The per-topic create facts for EventSub subscriptions (twitch-eventsub §3.3). One home for the condition
/// shape, the topic version, and the token-owner classification so the transport and service stay generic.
/// </summary>
public sealed class EventSubConditionBuilder : IEventSubConditionBuilder
{
    public IReadOnlyDictionary<string, string> BuildCondition(
        string eventType,
        string twitchBroadcasterUserId
    ) =>
        eventType switch
        {
            "channel.follow" => new Dictionary<string, string>
            {
                ["broadcaster_user_id"] = twitchBroadcasterUserId,
                ["moderator_user_id"] = twitchBroadcasterUserId,
            },
            "channel.raid" => new Dictionary<string, string>
            {
                ["to_broadcaster_user_id"] = twitchBroadcasterUserId,
            },
            "channel.chat.message"
            or "channel.chat.notification"
            or "channel.chat.message_delete" => new Dictionary<string, string>
            {
                ["broadcaster_user_id"] = twitchBroadcasterUserId,
                ["user_id"] = twitchBroadcasterUserId,
            },
            "channel.shoutout.create" or "channel.shoutout.receive" => new Dictionary<
                string,
                string
            >
            {
                ["broadcaster_user_id"] = twitchBroadcasterUserId,
                ["moderator_user_id"] = twitchBroadcasterUserId,
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
            _ => "1",
        };

    public bool RequiresBroadcasterToken(string eventType) =>
        BroadcasterScopedEvents.Contains(eventType);

    /// <summary>Topics that require the broadcaster's own user token (the bot/app token lacks authorization).</summary>
    private static readonly HashSet<string> BroadcasterScopedEvents =
    [
        "channel.follow",
        "channel.ban",
        "channel.unban",
        "channel.cheer",
        "channel.subscribe",
        "channel.subscription.gift",
        "channel.subscription.message",
        "channel.channel_points_custom_reward_redemption.add",
        "channel.channel_points_custom_reward_redemption.update",
        "channel.channel_points_custom_reward.add",
        "channel.channel_points_custom_reward.update",
        "channel.channel_points_custom_reward.remove",
        "channel.poll.begin",
        "channel.poll.end",
        "channel.prediction.begin",
        "channel.prediction.lock",
        "channel.prediction.end",
        "channel.hype_train.begin",
        "channel.hype_train.end",
        "channel.shoutout.create",
        "channel.shoutout.receive",
        "channel.chat.message",
        "channel.chat.message_delete",
    ];
}

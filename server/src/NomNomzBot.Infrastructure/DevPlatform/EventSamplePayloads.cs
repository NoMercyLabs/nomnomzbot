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
/// appear here; every other handleable event has no verified sample yet and is intentionally omitted (
/// <see cref="SdkTypeEmitter.EmitEventCatalog"/> leaves <c>SamplePayloadJson</c> null for those, pending a real
/// fixture — see the TODO list below).
/// </summary>
/// <remarks>
/// TODO: attach verified real fixtures for the remaining handleable events as translator tests are written or
/// located for them (e.g. channel.subscription.gift, channel.subscription.message, channel.cheer,
/// channel.poll.progress/.end, channel.prediction.progress/.end/.lock, channel.shoutout.create/.receive,
/// channel.channel_points_custom_reward(.update/.remove)/_automatic_reward_redemption.add,
/// stream.online/.offline, channel.chat.message, and the rest of <see cref="Application.DevPlatform.EventCatalog"/>).
/// Never fabricate a payload here — leave the event out until a real fixture is confirmed.
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
    };
}

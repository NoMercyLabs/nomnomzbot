// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Infrastructure.EventStore.LegacyImport;

namespace NomNomzBot.Infrastructure.Tests.EventStore.LegacyImport;

/// <summary>
/// Proves the legacy <c>ChannelEvents</c> → domain-event mapping against the REAL legacy payload shapes (sampled
/// from the owner's database.sqlite). Each case asserts the mapped event's data — type, the fields the projections
/// fold, the real Twitch event time pulled from the blob, and the deterministic idempotent EventId — not merely
/// that mapping returned non-null. The serialized payload is re-parsed and checked so it carries the field names
/// the analytics projections read (UserId/UserDisplayName/Bits/…), i.e. it is byte-shaped like a live capture.
/// </summary>
public sealed class LegacyChannelEventMapperTests
{
    private static readonly Guid Tenant = Guid.Parse("0192a000-0000-7000-8000-00000000aa01");
    private readonly LegacyChannelEventMapper _mapper = new();

    private static LegacyChannelEventRow Row(string type, string data, string id = "msg-1") =>
        new(
            Id: id,
            ChannelId: "39863651",
            UserId: "42660213",
            Type: type,
            Data: data,
            CreatedAt: new DateTime(2025, 8, 14, 17, 0, 0, DateTimeKind.Utc)
        );

    [Fact]
    public void Maps_follow_to_NewFollowerEvent_with_real_follow_time()
    {
        AppendEventRequest? request = _mapper.Map(
            Row(
                "channel.follow",
                """{"UserId":"1335549269","UserName":"NoMercyBot_","UserLogin":"nomercybot_","BroadcasterUserId":"39863651","FollowedAt":"2025-08-01T18:25:29.7584295+00:00"}"""
            ),
            Tenant
        );

        request.Should().NotBeNull();
        request!.EventType.Should().Be("NewFollowerEvent");
        request.Source.Should().Be("import");
        request.BroadcasterId.Should().Be(Tenant);
        request
            .OccurredAt.Should()
            .Be(
                DateTime.Parse("2025-08-01T18:25:29.7584295+00:00").ToUniversalTime(),
                "the event time is the in-payload FollowedAt, not the DB row time"
            );

        JObject payload = JObject.Parse(request.PayloadJson);
        payload["UserId"]!.Value<string>().Should().Be("1335549269");
        payload["UserDisplayName"]!.Value<string>().Should().Be("NoMercyBot_");
        payload["UserLogin"]!.Value<string>().Should().Be("nomercybot_");
        Guid.Parse(payload["BroadcasterId"]!.Value<string>()!).Should().Be(Tenant);
    }

    [Fact]
    public void Maps_subscribe_to_NewSubscriptionEvent_and_skips_gifted_recipient_rows()
    {
        AppendEventRequest? direct = _mapper.Map(
            Row(
                "channel.subscribe",
                """{"UserId":"42660213","UserName":"DukaSoft","BroadcasterUserId":"39863651","Tier":"1000","IsGift":false}"""
            ),
            Tenant
        );
        direct!.EventType.Should().Be("NewSubscriptionEvent");
        JObject.Parse(direct.PayloadJson)["Tier"]!.Value<string>().Should().Be("1000");

        // A gifted sub is already counted via the gifter's channel.subscription.gift row, so the recipient-side
        // channel.subscribe(IsGift=true) must NOT also count — otherwise the subscriber total double-counts.
        AppendEventRequest? gifted = _mapper.Map(
            Row(
                "channel.subscribe",
                """{"UserId":"42660213","UserName":"DukaSoft","Tier":"1000","IsGift":true}"""
            ),
            Tenant
        );
        gifted.Should().BeNull();
    }

    [Fact]
    public void Maps_resubscription_message_with_cumulative_months_and_text()
    {
        AppendEventRequest? request = _mapper.Map(
            Row(
                "channel.subscription.message",
                """{"UserId":"93038945","UserName":"jeroenvanwissen","Tier":"1000","Message":{"Text":"oops","Emotes":null},"CumulativeMonths":2,"StreakMonths":0,"DurationMonths":1}"""
            ),
            Tenant
        );

        request!.EventType.Should().Be("ResubscriptionEvent");
        JObject payload = JObject.Parse(request.PayloadJson);
        payload["CumulativeMonths"]!.Value<int>().Should().Be(2);
        payload["Message"]!.Value<string>().Should().Be("oops");
    }

    [Fact]
    public void Maps_gift_subscription_preserving_the_gift_count()
    {
        AppendEventRequest? request = _mapper.Map(
            Row(
                "channel.subscription.gift",
                """{"UserId":"42660213","UserName":"DukaSoft","Total":5,"Tier":"1000","CumulativeTotal":5,"IsAnonymous":false}"""
            ),
            Tenant
        );

        request!.EventType.Should().Be("GiftSubscriptionEvent");
        JObject payload = JObject.Parse(request.PayloadJson);
        payload["GiftCount"]!.Value<int>().Should().Be(5);
        payload["GifterUserId"]!.Value<string>().Should().Be("42660213");
        payload["IsAnonymous"]!.Value<bool>().Should().BeFalse();
    }

    [Fact]
    public void Maps_cheer_with_bit_amount_the_projection_reads()
    {
        AppendEventRequest? request = _mapper.Map(
            Row(
                "channel.cheer",
                """{"IsAnonymous":false,"UserId":"42660213","UserName":"DukaSoft","Message":"Cheer100 hi","Bits":100}"""
            ),
            Tenant
        );

        request!.EventType.Should().Be("CheerEvent");
        // The channel-daily projection reads the "Bits" field off the payload — it must be present and correct.
        JObject.Parse(request.PayloadJson)["Bits"]!
            .Value<long>()
            .Should()
            .Be(100);
    }

    [Fact]
    public void Maps_anonymous_cheer_without_crashing_on_null_user()
    {
        AppendEventRequest? request = _mapper.Map(
            Row("channel.cheer", """{"IsAnonymous":true,"UserId":null,"Message":"hi","Bits":50}"""),
            Tenant
        );

        request!.EventType.Should().Be("CheerEvent");
        JObject payload = JObject.Parse(request.PayloadJson);
        payload["IsAnonymous"]!.Value<bool>().Should().BeTrue();
        payload["Bits"]!.Value<long>().Should().Be(50);
        payload["UserId"]!.Value<string>().Should().Be("anonymous");
    }

    [Fact]
    public void Maps_raid_reading_the_raider_from_the_inverted_From_fields()
    {
        // The legacy raid row inverts actor/channel: the raider is FromBroadcasterUser*, not the row UserId.
        AppendEventRequest? request = _mapper.Map(
            Row(
                "channel.raid",
                """{"FromBroadcasterUserId":"80695883","FromBroadcasterUserName":"ApexPixelated","FromBroadcasterUserLogin":"apexpixelated","ToBroadcasterUserId":"39863651","Viewers":3}"""
            ),
            Tenant
        );

        request!.EventType.Should().Be("RaidEvent");
        JObject payload = JObject.Parse(request.PayloadJson);
        payload["FromUserId"]!.Value<string>().Should().Be("80695883");
        payload["FromDisplayName"]!.Value<string>().Should().Be("ApexPixelated");
        payload["ViewerCount"]!.Value<int>().Should().Be(3);
    }

    [Fact]
    public void Maps_moderator_add_and_remove_to_their_roster_events()
    {
        const string data =
            """{"UserId":"1335549269","UserName":"NoMercyBot_","UserLogin":"nomercybot_"}""";

        _mapper
            .Map(Row("channel.moderator.add", data), Tenant)!
            .EventType.Should()
            .Be("ModeratorAddedEvent");
        _mapper
            .Map(Row("channel.moderator.remove", data), Tenant)!
            .EventType.Should()
            .Be("ModeratorRemovedEvent");
    }

    [Fact]
    public void Maps_permanent_ban_to_UserBannedEvent()
    {
        AppendEventRequest? request = _mapper.Map(
            Row(
                "channel.ban",
                """{"UserId":"663947590","UserName":"Propz_tv","ModeratorUserId":"39863651","Reason":"temp for trying","BannedAt":"2025-09-29T10:54:08.5968148+00:00","EndsAt":null,"IsPermanent":true}"""
            ),
            Tenant
        );

        request!.EventType.Should().Be("UserBannedEvent");
        JObject payload = JObject.Parse(request.PayloadJson);
        payload["TargetUserId"]!.Value<string>().Should().Be("663947590");
        payload["Reason"]!.Value<string>().Should().Be("temp for trying");
    }

    [Fact]
    public void Maps_temporary_ban_to_UserTimedOutEvent_with_derived_duration()
    {
        AppendEventRequest? request = _mapper.Map(
            Row(
                "channel.ban",
                """{"UserId":"663947590","UserName":"Propz_tv","ModeratorUserId":"39863651","Reason":"cool down","BannedAt":"2025-09-29T10:00:00+00:00","EndsAt":"2025-09-29T10:10:00+00:00","IsPermanent":false}"""
            ),
            Tenant
        );

        request!.EventType.Should().Be("UserTimedOutEvent");
        JObject.Parse(request.PayloadJson)["DurationSeconds"]!.Value<int>().Should().Be(600);
    }

    [Fact]
    public void Maps_reward_redemption_with_reward_id_title_and_cost()
    {
        AppendEventRequest? request = _mapper.Map(
            Row(
                "channel.points.custom.reward.redemption.add",
                """{"Id":"b44c2590-d413-4335-8341-dc1537967837","UserId":"107107327","UserName":"mahybe","UserInput":"","Status":"unfulfilled","Reward":{"Id":"ac53c1f0-ac09-436d-a57c-21842f2bc828","Title":"Dunglish for 5 minutes","Cost":5000},"RedeemedAt":"2025-08-14T17:30:11.6612797+00:00"}"""
            ),
            Tenant
        );

        request!.EventType.Should().Be("RewardRedeemedEvent");
        JObject payload = JObject.Parse(request.PayloadJson);
        payload["RewardId"]!.Value<string>().Should().Be("ac53c1f0-ac09-436d-a57c-21842f2bc828");
        payload["RewardTitle"]!.Value<string>().Should().Be("Dunglish for 5 minutes");
        payload["Cost"]!.Value<int>().Should().Be(5000);
        payload["RedemptionId"]!
            .Value<string>()
            .Should()
            .Be("b44c2590-d413-4335-8341-dc1537967837");
    }

    [Fact]
    public void Maps_chat_message_to_ChatMessageReceivedEvent_with_the_REAL_twitch_message_id()
    {
        // The chat bulk (~28k rows) is the heart of the backfill. Its identity is the real Twitch MessageId GUID
        // inside the payload — NOT a synthetic UUIDv5 — so a live re-delivery of the same message dedupes against
        // the imported one. The legacy blob uses ChatterUser* fields and a nested Message.Text + Fragments[].
        const string messageId = "69957384-5a22-4536-a82b-c6d318bf12c7";
        AppendEventRequest? request = _mapper.Map(
            Row(
                "channel.chat.message",
                """{"MessageId":"69957384-5a22-4536-a82b-c6d318bf12c7","ChatterUserId":"128142135","ChatterUserName":"Kanawanagasaki","ChatterUserLogin":"kanawanagasaki","Color":"#008000","MessageType":"text","Message":{"Text":"kanawaSpin","Fragments":[{"Type":"emote","Text":"kanawaSpin","Emote":{"Id":"emotesv2_d29ad362da0343b38c75c2a7c95eb0a3","EmoteSetId":"321763748","OwnerId":"128142135","Format":["static","animated"]}}]},"Badges":[{"SetId":"lead_moderator","Id":"1","Info":""},{"SetId":"founder","Id":"0","Info":"4"}],"Cheer":null,"Reply":null,"IsSubscriber":true,"IsModerator":true,"IsBroadcaster":false,"IsVip":false}"""
            ),
            Tenant
        );

        request.Should().NotBeNull();
        request!.EventType.Should().Be("ChatMessageReceivedEvent");
        request.Source.Should().Be("import");
        request
            .EventId.Should()
            .Be(
                Guid.Parse(messageId),
                "the EventId IS the real Twitch message id GUID, not a derived UUIDv5"
            );

        JObject payload = JObject.Parse(request.PayloadJson);
        payload["MessageId"]!.Value<string>().Should().Be(messageId);
        payload["UserId"]!.Value<string>().Should().Be("128142135");
        payload["UserDisplayName"]!.Value<string>().Should().Be("Kanawanagasaki");
        payload["UserLogin"]!.Value<string>().Should().Be("kanawanagasaki");
        payload["Message"]!.Value<string>().Should().Be("kanawaSpin");
        payload["ColorHex"]!.Value<string>().Should().Be("#008000");
        payload["IsSubscriber"]!.Value<bool>().Should().BeTrue();
        payload["IsModerator"]!.Value<bool>().Should().BeTrue();
        // The structured fragment survives so inline emote rendering still works off the imported event.
        JArray fragments = (JArray)payload["Fragments"]!;
        fragments.Should().HaveCount(1);
        fragments[0]["Type"]!.Value<string>().Should().Be("emote");
        fragments[0]["EmoteId"]!
            .Value<string>()
            .Should()
            .Be("emotesv2_d29ad362da0343b38c75c2a7c95eb0a3");
    }

    [Fact]
    public void Maps_chat_message_reply_and_cheer_bits()
    {
        AppendEventRequest? request = _mapper.Map(
            Row(
                "channel.chat.message",
                """{"MessageId":"2fed320c-0acc-4e2d-a701-973500b0b49e","ChatterUserId":"100","ChatterUserName":"Bob","ChatterUserLogin":"bob","Color":null,"MessageType":"text","Message":{"Text":"+1","Fragments":[{"Type":"text","Text":"+1"}]},"Badges":[],"Cheer":{"Bits":100},"Reply":{"ParentMessageId":"73d98088-09cc-4bca-932f-250a0b2dafe5","ParentMessageBody":"hello","ParentUserName":"Stoney_Eagle"},"IsSubscriber":false,"IsModerator":false,"IsBroadcaster":false,"IsVip":false}"""
            ),
            Tenant
        );

        JObject payload = JObject.Parse(request!.PayloadJson);
        payload["Bits"]!.Value<int>().Should().Be(100);
        payload["ReplyParentMessageId"]!
            .Value<string>()
            .Should()
            .Be("73d98088-09cc-4bca-932f-250a0b2dafe5");
        payload["ReplyParentUserName"]!.Value<string>().Should().Be("Stoney_Eagle");
    }

    [Fact]
    public void Maps_chat_notification_to_a_chat_message_keyed_on_its_real_message_id()
    {
        // A chat notification (announcement/watch_streak/…) is a chat-rendered system line with a real MessageId.
        // It maps to a chat message so it surfaces in the feed WITHOUT folding into sub/raid analytics (those are
        // already counted by the dedicated channel.subscribe/channel.raid topics — no double count).
        AppendEventRequest? request = _mapper.Map(
            Row(
                "channel.chat.notification",
                """{"MessageId":"2bf63cf6-33aa-4b20-a303-dd3016acf792","ChatterUserId":"39863651","ChatterUserName":"Stoney_Eagle","ChatterUserLogin":"stoney_eagle","ChatterIsAnonymous":false,"Color":"#BF0039","Badges":[],"NoticeType":"announcement","SystemMessage":"","Message":{"Text":"Yo peep this","Fragments":[{"Type":"text","Text":"Yo peep this"}]}}"""
            ),
            Tenant
        );

        request.Should().NotBeNull();
        request!.EventType.Should().Be("ChatMessageReceivedEvent");
        request.EventId.Should().Be(Guid.Parse("2bf63cf6-33aa-4b20-a303-dd3016acf792"));
        JObject payload = JObject.Parse(request.PayloadJson);
        payload["UserId"]!.Value<string>().Should().Be("39863651");
        payload["Message"]!.Value<string>().Should().Be("Yo peep this");
    }

    [Fact]
    public void Skips_an_anonymous_chat_notification_with_no_chatter_to_attribute()
    {
        // The rare anonymous gift notice carries no ChatterUserId — there is no viewer to attribute, and the gift
        // it announces is already counted by the gifter's channel.subscription.gift row, so it is a logged skip.
        _mapper
            .Map(
                Row(
                    "channel.chat.notification",
                    """{"MessageId":"15faec51-d953-4d4f-92e9-cb25cd4962ff","ChatterUserId":null,"ChatterIsAnonymous":true,"NoticeType":"sub_gift","Message":{"Text":""}}"""
                ),
                Tenant
            )
            .Should()
            .BeNull();
    }

    [Fact]
    public void Reward_redemption_EventId_is_the_real_twitch_redemption_id()
    {
        // The redemption carries its own real Twitch GUID (the redemption Id) — use it as the identity so the
        // imported row dedupes against a live redemption delivery, rather than a derived UUIDv5.
        AppendEventRequest? request = _mapper.Map(
            Row(
                "channel.points.custom.reward.redemption.add",
                """{"Id":"b44c2590-d413-4335-8341-dc1537967837","UserId":"107107327","UserName":"mahybe","UserInput":"","Status":"unfulfilled","Reward":{"Id":"ac53c1f0-ac09-436d-a57c-21842f2bc828","Title":"Dunglish","Cost":5000},"RedeemedAt":"2025-08-14T17:30:11.6612797+00:00"}"""
            ),
            Tenant
        );

        request!.EventId.Should().Be(Guid.Parse("b44c2590-d413-4335-8341-dc1537967837"));
    }

    [Fact]
    public void Derives_a_stable_idempotent_EventId_from_tenant_and_legacy_id()
    {
        LegacyChannelEventRow row = Row(
            "channel.follow",
            """{"UserId":"1","UserName":"a","UserLogin":"a","FollowedAt":"2025-08-01T18:25:29+00:00"}""",
            id: "stable-message-id"
        );

        Guid first = _mapper.Map(row, Tenant)!.EventId;
        Guid again = _mapper.Map(row, Tenant)!.EventId;
        Guid otherTenant = _mapper.Map(row, Guid.NewGuid())!.EventId;

        first.Should().NotBeEmpty();
        again
            .Should()
            .Be(
                first,
                "the same legacy row always derives the same EventId (idempotent re-import)"
            );
        otherTenant
            .Should()
            .NotBe(first, "a different tenant importing the same legacy id never collides");
    }

    [Theory]
    [InlineData("channel.update")]
    [InlineData("channel.points.custom.reward.update")]
    [InlineData("channel.poll.progress")]
    [InlineData("websocket.error")]
    [InlineData("channel.vip.add")]
    public void Skips_unimported_and_noise_types(string type)
    {
        _mapper.Map(Row(type, "{}"), Tenant).Should().BeNull();
    }

    [Fact]
    public void Skips_a_row_whose_payload_is_unparseable_or_missing_required_fields()
    {
        _mapper.Map(Row("channel.follow", "not json"), Tenant).Should().BeNull();
        _mapper.Map(Row("channel.follow", """{"UserName":"no id"}"""), Tenant).Should().BeNull();
    }
}

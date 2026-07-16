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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Infrastructure.Chat.Kick;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Chat.Kick;

/// <summary>
/// Proves the Kick webhook translation onto the ONE substrate. Chat: a verified
/// <c>chat.message.sent</c> resolves the kick-provider tenant and publishes the canonical
/// <see cref="ChatMessageReceivedEvent"/> with the exact identity, role flags, and provider key; a
/// redelivered id publishes nothing; unknown broadcasters and malformed bodies are skipped, never
/// thrown. Community/monetization: follows, subs (new/renewal/gifts), reward-redemption updates,
/// moderation bans/timeouts, and kicks gifts publish the SAME canonical domain events their Twitch
/// EventSub twins do. Livestream: status stamps the tenant's <c>IsLive</c> (+ title) behind
/// <c>platformsLive</c>; metadata rides the canonical <see cref="ChannelUpdatedEvent"/>. An event type
/// without a consumer is a silent no-op.
/// </summary>
public sealed class KickWebhookIngestTests
{
    private static readonly Guid Tenant = Guid.Parse("0192d000-0000-7000-8000-0000000000a1");
    private static readonly Guid Owner = Guid.Parse("0192d000-0000-7000-8000-0000000000a9");

    private const string Broadcaster = """
        { "user_id": 12345, "username": "StreamerGal", "channel_slug": "streamergal" }
        """;

    private const string ChatBody = $$"""
        {
          "message_id": "kick-msg-1",
          "broadcaster": {{Broadcaster}},
          "sender": {
            "user_id": 678,
            "username": "ChatterBoi",
            "channel_slug": "chatterboi",
            "identity": { "username_color": "#FF0000", "badges": [ { "text": "Moderator", "type": "moderator" }, { "text": "Sub", "type": "subscriber", "count": 3 } ] }
          },
          "content": "hello kick [emote:37226:EZ]",
          "emotes": [ { "emote_id": "37226", "positions": [ { "s": 11, "e": 26 } ] } ],
          "created_at": "2026-07-11T12:34:56Z"
        }
        """;

    private static (KickWebhookIngest Ingest, AuthDbContext Db, RecordingEventBus Bus) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new Channel
            {
                Id = Tenant,
                OwnerUserId = Owner,
                Provider = AuthEnums.Platform.Kick,
                ExternalChannelId = "12345",
                Name = "streamergal",
                NameNormalized = "streamergal",
                IsOnboarded = true,
                DeploymentMode = AuthEnums.DeploymentMode.Saas,
                BillingTierKey = "free",
            }
        );
        db.SaveChanges();

        RecordingEventBus bus = new();
        KickWebhookIngest ingest = new(
            db,
            bus,
            TimeProvider.System,
            NullLogger<KickWebhookIngest>.Instance
        );
        return (ingest, db, bus);
    }

    // ─── chat.message.sent ───────────────────────────────────────────────────

    [Fact]
    public async Task A_chat_message_publishes_the_canonical_event_with_kick_identity_and_roles()
    {
        (KickWebhookIngest ingest, _, RecordingEventBus bus) = Build();

        await ingest.HandleAsync("chat.message.sent", ChatBody);

        ChatMessageReceivedEvent published = bus
            .Published.OfType<ChatMessageReceivedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(Tenant);
        published.Provider.Should().Be(AuthEnums.Platform.Kick);
        published.MessageId.Should().Be("kick-msg-1");
        published.UserId.Should().Be("678");
        published.UserDisplayName.Should().Be("ChatterBoi");
        published.UserLogin.Should().Be("chatterboi", "the channel slug is the stable handle");
        published.Message.Should().Be("hello kick [emote:37226:EZ]");
        published.OccurredAt.Should().Be(DateTimeOffset.Parse("2026-07-11T12:34:56Z"));
        published.IsModerator.Should().BeTrue("the moderator badge is present");
        published.IsSubscriber.Should().BeTrue("the subscriber badge is present");
        published.IsBroadcaster.Should().BeFalse();
        published.IsVip.Should().BeFalse();
    }

    [Fact]
    public async Task The_broadcasters_own_message_flags_is_broadcaster_by_identity_match()
    {
        (KickWebhookIngest ingest, _, RecordingEventBus bus) = Build();
        string body = ChatBody.Replace("\"user_id\": 678", "\"user_id\": 12345");

        await ingest.HandleAsync("chat.message.sent", body);

        bus.Published.OfType<ChatMessageReceivedEvent>()
            .Single()
            .IsBroadcaster.Should()
            .BeTrue("sender.user_id equals broadcaster.user_id");
    }

    [Fact]
    public async Task An_unknown_broadcaster_is_skipped()
    {
        (KickWebhookIngest ingest, _, RecordingEventBus bus) = Build();
        string body = ChatBody.Replace("12345", "99999");

        await ingest.HandleAsync("chat.message.sent", body);

        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task A_redelivered_message_id_publishes_nothing()
    {
        (KickWebhookIngest ingest, AuthDbContext db, RecordingEventBus bus) = Build();
        db.ChatMessages.Add(
            new ChatMessage
            {
                Id = "kick-msg-1",
                BroadcasterId = Tenant,
                UserId = "678",
                Username = "chatterboi",
                DisplayName = "ChatterBoi",
                UserType = "viewer",
                Message = "hello kick [emote:37226:EZ]",
            }
        );
        db.SaveChanges();

        await ingest.HandleAsync("chat.message.sent", ChatBody);

        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task A_malformed_body_is_swallowed_not_thrown()
    {
        (KickWebhookIngest ingest, _, RecordingEventBus bus) = Build();

        await ingest.HandleAsync("chat.message.sent", "not json at all");
        await ingest.HandleAsync("chat.message.sent", """{"message_id":"x"}""");
        await ingest.HandleAsync("channel.followed", "not json at all");
        await ingest.HandleAsync("kicks.gifted", """{"gift":{"amount":5}}""");

        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task An_event_type_without_a_consumer_is_a_silent_no_op()
    {
        (KickWebhookIngest ingest, _, RecordingEventBus bus) = Build();

        await ingest.HandleAsync("some.future.event", """{"anything":true}""");

        bus.Published.Should().BeEmpty();
    }

    // ─── livestream.status.updated — Kick's live tracker behind platformsLive ───

    private const string LiveBody = $$"""
        {
          "broadcaster": {{Broadcaster}},
          "is_live": true,
          "title": "Bird up!",
          "started_at": "2026-07-16T09:00:00Z",
          "ended_at": null
        }
        """;

    [Fact]
    public async Task A_livestream_going_live_stamps_the_tenant_live_with_its_title()
    {
        (KickWebhookIngest ingest, AuthDbContext db, _) = Build();

        await ingest.HandleAsync("livestream.status.updated", LiveBody);

        Channel tenant = db.Channels.Single(c => c.Id == Tenant);
        tenant.IsLive.Should().BeTrue();
        tenant.Title.Should().Be("Bird up!");
    }

    [Fact]
    public async Task A_livestream_ending_clears_the_tenant_live_flag()
    {
        (KickWebhookIngest ingest, AuthDbContext db, _) = Build();
        db.Channels.Single(c => c.Id == Tenant).IsLive = true;
        db.SaveChanges();
        string body = LiveBody
            .Replace("\"is_live\": true", "\"is_live\": false")
            .Replace("\"ended_at\": null", "\"ended_at\": \"2026-07-16T11:00:00Z\"");

        await ingest.HandleAsync("livestream.status.updated", body);

        db.Channels.Single(c => c.Id == Tenant).IsLive.Should().BeFalse();
    }

    [Fact]
    public async Task A_livestream_status_for_an_unknown_broadcaster_is_skipped()
    {
        (KickWebhookIngest ingest, AuthDbContext db, _) = Build();
        string body = LiveBody.Replace("12345", "99999");

        await ingest.HandleAsync("livestream.status.updated", body);

        db.Channels.Single(c => c.Id == Tenant).IsLive.Should().BeFalse();
    }

    // ─── livestream.metadata.updated → canonical ChannelUpdatedEvent ─────────

    [Fact]
    public async Task A_metadata_update_publishes_the_canonical_channel_updated_event()
    {
        (KickWebhookIngest ingest, _, RecordingEventBus bus) = Build();
        string body = $$"""
            {
              "broadcaster": {{Broadcaster}},
              "metadata": {
                "title": "New title!",
                "language": "en",
                "has_mature_content": false,
                "category": { "id": 5, "name": "Just Chatting", "thumbnail": "https://x/t.png" }
              }
            }
            """;

        await ingest.HandleAsync("livestream.metadata.updated", body);

        ChannelUpdatedEvent updated = bus
            .Published.OfType<ChannelUpdatedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        updated.BroadcasterId.Should().Be(Tenant);
        updated.BroadcasterDisplayName.Should().Be("StreamerGal");
        updated.NewTitle.Should().Be("New title!");
        updated.NewGameName.Should().Be("Just Chatting");
    }

    // ─── channel.followed → canonical FollowEvent ────────────────────────────

    [Fact]
    public async Task A_follow_publishes_the_canonical_follow_event()
    {
        (KickWebhookIngest ingest, _, RecordingEventBus bus) = Build();
        string body = $$"""
            {
              "broadcaster": {{Broadcaster}},
              "follower": { "user_id": 777, "username": "NewFan", "channel_slug": "newfan" }
            }
            """;

        await ingest.HandleAsync("channel.followed", body);

        FollowEvent followed = bus.Published.OfType<FollowEvent>().Should().ContainSingle().Subject;
        followed.BroadcasterId.Should().Be(Tenant);
        followed.UserId.Should().Be("777");
        followed.UserDisplayName.Should().Be("NewFan");
        followed.UserLogin.Should().Be("newfan");
    }

    // ─── channel.subscription.* → canonical sub events ───────────────────────

    [Fact]
    public async Task A_new_subscription_publishes_the_canonical_event_at_the_base_tier()
    {
        (KickWebhookIngest ingest, _, RecordingEventBus bus) = Build();
        string body = $$"""
            {
              "broadcaster": {{Broadcaster}},
              "subscriber": { "user_id": 888, "username": "SubGuy", "channel_slug": "subguy" },
              "duration": 1,
              "created_at": "2026-07-16T10:00:00Z",
              "expires_at": "2026-08-16T10:00:00Z"
            }
            """;

        await ingest.HandleAsync("channel.subscription.new", body);

        NewSubscriptionEvent subscribed = bus
            .Published.OfType<NewSubscriptionEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        subscribed.BroadcasterId.Should().Be(Tenant);
        subscribed.UserId.Should().Be("888");
        subscribed.UserDisplayName.Should().Be("SubGuy");
        subscribed.Tier.Should().Be("1000", "Kick subs are untiered — mapped to the base tier");
        subscribed.OccurredAt.Should().Be(DateTimeOffset.Parse("2026-07-16T10:00:00Z"));
    }

    [Fact]
    public async Task A_renewal_publishes_the_canonical_resubscription_with_honest_months()
    {
        (KickWebhookIngest ingest, _, RecordingEventBus bus) = Build();
        string body = $$"""
            {
              "broadcaster": {{Broadcaster}},
              "subscriber": { "user_id": 888, "username": "SubGuy", "channel_slug": "subguy" },
              "duration": 7,
              "created_at": "2026-07-16T10:00:00Z",
              "expires_at": "2026-08-16T10:00:00Z"
            }
            """;

        await ingest.HandleAsync("channel.subscription.renewal", body);

        ResubscriptionEvent resubscribed = bus
            .Published.OfType<ResubscriptionEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        resubscribed.BroadcasterId.Should().Be(Tenant);
        resubscribed.UserId.Should().Be("888");
        resubscribed.CumulativeMonths.Should().Be(7, "Kick's duration is months subscribed");
        resubscribed.StreakMonths.Should().Be(0, "Kick reports no streak — never invent one");
        resubscribed.Message.Should().BeNull("Kick renewals carry no resub message");
    }

    [Fact]
    public async Task A_gift_drop_publishes_the_canonical_gift_event_with_its_recipients()
    {
        (KickWebhookIngest ingest, _, RecordingEventBus bus) = Build();
        string body = $$"""
            {
              "broadcaster": {{Broadcaster}},
              "gifter": { "is_anonymous": false, "user_id": 900, "username": "GenerousGal", "channel_slug": "generousgal" },
              "giftees": [
                { "user_id": 901, "username": "LuckyOne", "channel_slug": "luckyone" },
                { "user_id": 902, "username": "LuckyTwo", "channel_slug": "luckytwo" }
              ],
              "created_at": "2026-07-16T10:00:00Z",
              "expires_at": "2026-08-16T10:00:00Z"
            }
            """;

        await ingest.HandleAsync("channel.subscription.gifts", body);

        GiftSubscriptionEvent gifted = bus
            .Published.OfType<GiftSubscriptionEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        gifted.BroadcasterId.Should().Be(Tenant);
        gifted.GifterUserId.Should().Be("900");
        gifted.GifterDisplayName.Should().Be("GenerousGal");
        gifted.GiftCount.Should().Be(2);
        gifted.IsAnonymous.Should().BeFalse();
        gifted
            .Recipients.Should()
            .Equal(
                [new GiftRecipient("901", "LuckyOne"), new GiftRecipient("902", "LuckyTwo")],
                "Kick enumerates the recipients on the event itself"
            );
    }

    [Fact]
    public async Task An_anonymous_gift_drop_carries_the_flag_and_empty_gifter_identity()
    {
        (KickWebhookIngest ingest, _, RecordingEventBus bus) = Build();
        string body = $$"""
            {
              "broadcaster": {{Broadcaster}},
              "gifter": { "is_anonymous": true, "user_id": null, "username": null, "channel_slug": null },
              "giftees": [ { "user_id": 901, "username": "LuckyOne", "channel_slug": "luckyone" } ],
              "created_at": "2026-07-16T10:00:00Z"
            }
            """;

        await ingest.HandleAsync("channel.subscription.gifts", body);

        GiftSubscriptionEvent gifted = bus.Published.OfType<GiftSubscriptionEvent>().Single();
        gifted.IsAnonymous.Should().BeTrue();
        gifted.GifterUserId.Should().BeEmpty("the Twitch translator convention for anonymous");
        gifted.GifterDisplayName.Should().BeEmpty();
        gifted.GiftCount.Should().Be(1);
    }

    // ─── channel.reward.redemption.updated → canonical redemption update ─────

    [Fact]
    public async Task An_accepted_redemption_publishes_the_canonical_fulfilled_update()
    {
        (KickWebhookIngest ingest, _, RecordingEventBus bus) = Build();

        await ingest.HandleAsync("channel.reward.redemption.updated", RedemptionBody("accepted"));

        RewardRedemptionUpdatedEvent updated = bus
            .Published.OfType<RewardRedemptionUpdatedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        updated.BroadcasterId.Should().Be(Tenant);
        updated.RedemptionId.Should().Be("red-1");
        updated.RewardId.Should().Be("rew-1");
        updated.RewardTitle.Should().Be("Hydrate!");
        updated.UserId.Should().Be("678");
        updated.Status.Should().Be("fulfilled", "Kick's accepted maps to the canonical fulfilled");
    }

    [Fact]
    public async Task A_rejected_redemption_publishes_the_canonical_canceled_update()
    {
        (KickWebhookIngest ingest, _, RecordingEventBus bus) = Build();

        await ingest.HandleAsync("channel.reward.redemption.updated", RedemptionBody("rejected"));

        bus.Published.OfType<RewardRedemptionUpdatedEvent>()
            .Single()
            .Status.Should()
            .Be("canceled");
    }

    [Fact]
    public async Task A_still_pending_redemption_publishes_nothing()
    {
        // pending is the queued state, not a completed transition — the canonical event models only
        // fulfilled/canceled.
        (KickWebhookIngest ingest, _, RecordingEventBus bus) = Build();

        await ingest.HandleAsync("channel.reward.redemption.updated", RedemptionBody("pending"));

        bus.Published.Should().BeEmpty();
    }

    private static string RedemptionBody(string status) =>
        $$"""
            {
              "id": "red-1",
              "user_input": "with ice please",
              "status": "{{status}}",
              "redeemed_at": "2026-07-16T10:00:00Z",
              "reward": { "id": "rew-1", "title": "Hydrate!", "cost": 100, "description": "drink" },
              "redeemer": { "user_id": 678, "username": "ChatterBoi", "channel_slug": "chatterboi" },
              "broadcaster": {{Broadcaster}}
            }
            """;

    // ─── moderation.banned → canonical ban / timeout ─────────────────────────

    [Fact]
    public async Task A_permanent_ban_publishes_the_canonical_banned_event()
    {
        (KickWebhookIngest ingest, _, RecordingEventBus bus) = Build();
        string body = $$"""
            {
              "broadcaster": {{Broadcaster}},
              "moderator": { "user_id": 555, "username": "ModMan", "channel_slug": "modman" },
              "banned_user": { "user_id": 666, "username": "BadActor", "channel_slug": "badactor" },
              "metadata": { "reason": "spam", "created_at": "2026-07-16T10:00:00Z", "expires_at": null }
            }
            """;

        await ingest.HandleAsync("moderation.banned", body);

        UserBannedEvent banned = bus
            .Published.OfType<UserBannedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        banned.BroadcasterId.Should().Be(Tenant);
        banned.TargetUserId.Should().Be("666");
        banned.TargetDisplayName.Should().Be("BadActor");
        banned.ModeratorUserId.Should().Be("555");
        banned.Reason.Should().Be("spam");
        bus.Published.OfType<UserTimedOutEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task A_temporary_ban_publishes_the_canonical_timeout_with_its_real_duration()
    {
        (KickWebhookIngest ingest, _, RecordingEventBus bus) = Build();
        string body = $$"""
            {
              "broadcaster": {{Broadcaster}},
              "moderator": { "user_id": 555, "username": "ModMan", "channel_slug": "modman" },
              "banned_user": { "user_id": 666, "username": "BadActor", "channel_slug": "badactor" },
              "metadata": { "reason": "cool off", "created_at": "2026-07-16T10:00:00Z", "expires_at": "2026-07-16T10:10:00Z" }
            }
            """;

        await ingest.HandleAsync("moderation.banned", body);

        UserTimedOutEvent timedOut = bus
            .Published.OfType<UserTimedOutEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        timedOut.TargetUserId.Should().Be("666");
        timedOut.DurationSeconds.Should().Be(600, "expires_at minus created_at is the timeout");
        timedOut.Reason.Should().Be("cool off");
        bus.Published.OfType<UserBannedEvent>().Should().BeEmpty();
    }

    // ─── kicks.gifted → canonical CheerEvent ─────────────────────────────────

    [Fact]
    public async Task A_kicks_gift_publishes_the_canonical_cheer_with_the_kicks_amount()
    {
        (KickWebhookIngest ingest, _, RecordingEventBus bus) = Build();
        string body = $$"""
            {
              "broadcaster": {{Broadcaster}},
              "sender": { "user_id": 678, "username": "ChatterBoi", "channel_slug": "chatterboi" },
              "gift": { "amount": 250, "name": "Rocket", "type": "fun", "tier": "gold", "message": "great stream!", "pinned_time_seconds": 60 },
              "created_at": "2026-07-16T10:00:00Z"
            }
            """;

        await ingest.HandleAsync("kicks.gifted", body);

        CheerEvent cheered = bus.Published.OfType<CheerEvent>().Should().ContainSingle().Subject;
        cheered.BroadcasterId.Should().Be(Tenant);
        cheered.UserId.Should().Be("678");
        cheered.UserDisplayName.Should().Be("ChatterBoi");
        cheered.Bits.Should().Be(250, "kicks are Kick's bits analog — the amount carries as-is");
        cheered.Message.Should().Be("great stream!");
        cheered.IsAnonymous.Should().BeFalse();
    }
}

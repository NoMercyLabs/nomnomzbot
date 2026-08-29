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
using NomNomzBot.Application.Contracts.YouTube;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Infrastructure.Chat.YouTube;

namespace NomNomzBot.Infrastructure.Tests.Chat.YouTube;

/// <summary>
/// S030-a — proves <see cref="YouTubeLiveChatEventTranslator"/> maps each of the six real
/// Google-documented <c>snippet.type</c> supporter events to the SAME canonical domain event Twitch
/// EventSub / Kick's webhook ingest publish for the equivalent concept (supporter-events.md §4.1), with
/// the actual field values carried over — not merely "an event was raised".
/// </summary>
public sealed class YouTubeLiveChatEventTranslatorTests
{
    private static readonly Guid TenantId = Guid.Parse("0199b000-0000-7000-8000-0000000000d1");
    private static readonly DateTimeOffset PublishedAt = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_plain_text_message_translates_to_nothing()
    {
        YouTubeLiveChatMessage message = Message("textMessageEvent");

        YouTubeLiveChatEventTranslator.Translate(message, TenantId).Should().BeNull();
    }

    [Fact]
    public void A_gift_membership_received_message_translates_to_nothing()
    {
        // Not in the §4.1 map — the gifter's membershipGiftingEvent already published the one
        // GiftSubscriptionEvent for the batch; publishing again here would double-count it.
        YouTubeLiveChatMessage message = Message(
            "giftMembershipReceivedEvent",
            giftMembershipReceivedDetails: new("Gold Member", "UCgifter", "mg-1")
        );

        YouTubeLiveChatEventTranslator.Translate(message, TenantId).Should().BeNull();
    }

    [Fact]
    public void A_super_chat_maps_to_a_provider_scoped_cheer_event_with_the_amount_in_minor_units()
    {
        YouTubeLiveChatMessage message = Message(
            "superChatEvent",
            superChatDetails: new(
                AmountMicros: 5_000_000, // $5.00
                Currency: "USD",
                AmountDisplayString: "$5.00",
                UserComment: "great stream!",
                Tier: 2
            )
        );

        IProviderScopedEvent? result = YouTubeLiveChatEventTranslator.Translate(message, TenantId);

        CheerEvent cheer = result.Should().BeOfType<CheerEvent>().Subject;
        cheer.Provider.Should().Be(AuthEnums.Platform.YouTube);
        cheer.BroadcasterId.Should().Be(TenantId);
        cheer.OccurredAt.Should().Be(PublishedAt);
        cheer.UserId.Should().Be("UCauthor");
        cheer.UserDisplayName.Should().Be("Author");
        cheer.Bits.Should().Be(500, "5,000,000 micros / 10,000 == 500 minor units ($5.00)");
        cheer.Message.Should().Be("great stream!");
        cheer.IsAnonymous.Should().BeFalse();
    }

    [Fact]
    public void A_super_sticker_maps_to_a_provider_scoped_cheer_event_carrying_the_sticker_alt_text()
    {
        YouTubeLiveChatMessage message = Message(
            "superStickerEvent",
            superStickerDetails: new(
                StickerId: "sticker-42",
                AltText: ":heart:",
                AmountMicros: 2_000_000, // $2.00
                Currency: "USD",
                AmountDisplayString: "$2.00",
                Tier: 1
            )
        );

        IProviderScopedEvent? result = YouTubeLiveChatEventTranslator.Translate(message, TenantId);

        CheerEvent cheer = result.Should().BeOfType<CheerEvent>().Subject;
        cheer.Provider.Should().Be(AuthEnums.Platform.YouTube);
        cheer.Bits.Should().Be(200, "2,000,000 micros / 10,000 == 200 minor units ($2.00)");
        cheer.Message.Should().Be(":heart:");
        cheer.IsAnonymous.Should().BeFalse();
    }

    [Fact]
    public void A_new_sponsor_maps_to_a_provider_scoped_new_subscription_event()
    {
        YouTubeLiveChatMessage message = Message(
            "newSponsorEvent",
            newSponsorDetails: new("Bronze Member", IsUpgrade: false)
        );

        IProviderScopedEvent? result = YouTubeLiveChatEventTranslator.Translate(message, TenantId);

        NewSubscriptionEvent sub = result.Should().BeOfType<NewSubscriptionEvent>().Subject;
        sub.Provider.Should().Be(AuthEnums.Platform.YouTube);
        sub.BroadcasterId.Should().Be(TenantId);
        sub.OccurredAt.Should().Be(PublishedAt);
        sub.UserId.Should().Be("UCauthor");
        sub.UserDisplayName.Should().Be("Author");
        sub.Tier.Should().Be("Bronze Member");
    }

    [Fact]
    public void A_member_milestone_maps_to_a_provider_scoped_resubscription_event_with_the_member_month()
    {
        YouTubeLiveChatMessage message = Message(
            "memberMilestoneChatEvent",
            memberMilestoneChatDetails: new("6 months strong!", MemberMonth: 6, "Silver Member")
        );

        IProviderScopedEvent? result = YouTubeLiveChatEventTranslator.Translate(message, TenantId);

        ResubscriptionEvent resub = result.Should().BeOfType<ResubscriptionEvent>().Subject;
        resub.Provider.Should().Be(AuthEnums.Platform.YouTube);
        resub.BroadcasterId.Should().Be(TenantId);
        resub.UserId.Should().Be("UCauthor");
        resub.UserDisplayName.Should().Be("Author");
        resub.Tier.Should().Be("Silver Member");
        resub.CumulativeMonths.Should().Be(6);
        resub.StreakMonths.Should().Be(0, "YouTube does not report streaks — never invent one");
        resub.Message.Should().Be("6 months strong!");
    }

    [Fact]
    public void A_member_milestone_with_no_comment_carries_a_null_message()
    {
        YouTubeLiveChatMessage message = Message(
            "memberMilestoneChatEvent",
            memberMilestoneChatDetails: new(string.Empty, MemberMonth: 12, "Gold Member")
        );

        ResubscriptionEvent resub = YouTubeLiveChatEventTranslator
            .Translate(message, TenantId)
            .Should()
            .BeOfType<ResubscriptionEvent>()
            .Subject;

        resub.Message.Should().BeNull();
    }

    [Fact]
    public void A_membership_gifting_batch_maps_to_a_provider_scoped_gift_subscription_event()
    {
        YouTubeLiveChatMessage message = Message(
            "membershipGiftingEvent",
            membershipGiftingDetails: new(GiftMembershipsCount: 5, "Gold Member")
        );

        IProviderScopedEvent? result = YouTubeLiveChatEventTranslator.Translate(message, TenantId);

        GiftSubscriptionEvent gift = result.Should().BeOfType<GiftSubscriptionEvent>().Subject;
        gift.Provider.Should().Be(AuthEnums.Platform.YouTube);
        gift.BroadcasterId.Should().Be(TenantId);
        gift.GifterUserId.Should().Be("UCauthor");
        gift.GifterDisplayName.Should().Be("Author");
        gift.Tier.Should().Be("Gold Member");
        gift.GiftCount.Should().Be(5);
        gift.IsAnonymous.Should().BeFalse();
        gift.Recipients.Should()
            .BeEmpty("YouTube does not enumerate recipients on the gifter's message, unlike Kick");
    }

    private static YouTubeLiveChatMessage Message(
        string snippetType,
        YouTubeSuperChatDetails? superChatDetails = null,
        YouTubeSuperStickerDetails? superStickerDetails = null,
        YouTubeNewSponsorDetails? newSponsorDetails = null,
        YouTubeMemberMilestoneChatDetails? memberMilestoneChatDetails = null,
        YouTubeMembershipGiftingDetails? membershipGiftingDetails = null,
        YouTubeGiftMembershipReceivedDetails? giftMembershipReceivedDetails = null
    ) =>
        new(
            Id: "msg-1",
            AuthorChannelId: "UCauthor",
            AuthorDisplayName: "Author",
            DisplayText: "text",
            PublishedAt: PublishedAt,
            IsModerator: false,
            IsOwner: false,
            IsMember: true,
            SnippetType: snippetType,
            SuperChatDetails: superChatDetails,
            SuperStickerDetails: superStickerDetails,
            NewSponsorDetails: newSponsorDetails,
            MemberMilestoneChatDetails: memberMilestoneChatDetails,
            MembershipGiftingDetails: membershipGiftingDetails,
            GiftMembershipReceivedDetails: giftMembershipReceivedDetails
        );
}

// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Contracts.YouTube;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Rewards.Events;

namespace NomNomzBot.Infrastructure.Chat.YouTube;

/// <summary>
/// S030-a — translates the six YouTube <c>snippet.type</c> supporter events the poll worker already reads
/// (<see cref="YouTubeLiveChatMessage"/>) into the SAME canonical domain events Twitch EventSub and Kick's
/// webhook ingest publish for the equivalent concept, per <c>supporter-events.md</c> §4.1:
/// <list type="bullet">
/// <item><c>superChatEvent</c> / <c>superStickerEvent</c> → <see cref="CheerEvent"/> (Bits = paid amount
/// in minor currency units)</item>
/// <item><c>newSponsorEvent</c> → <see cref="NewSubscriptionEvent"/></item>
/// <item><c>memberMilestoneChatEvent</c> → <see cref="ResubscriptionEvent"/></item>
/// <item><c>membershipGiftingEvent</c> → <see cref="GiftSubscriptionEvent"/></item>
/// </list>
/// A plain <c>textMessageEvent</c> and <c>giftMembershipReceivedEvent</c> (the individual recipient's
/// "you received a gift" line — not in the §4.1 map, since the gifter's <c>membershipGiftingEvent</c>
/// already published the one <see cref="GiftSubscriptionEvent"/> for the batch) translate to <c>null</c> —
/// the caller publishes them alongside, never instead of, the plain <see cref="Domain.Chat.Events.ChatMessageReceivedEvent"/>.
/// </summary>
public static class YouTubeLiveChatEventTranslator
{
    /// <summary>Amount-in-micros to minor currency units (cents): 1,000,000 micros == 1 major unit == 100 minor units.</summary>
    private const ulong MicrosPerMinorUnit = 10_000;

    public static IProviderScopedEvent? Translate(YouTubeLiveChatMessage message, Guid tenantId) =>
        message.SnippetType switch
        {
            "superChatEvent" when message.SuperChatDetails is { } superChat => new CheerEvent
            {
                BroadcasterId = tenantId,
                Provider = AuthEnums.Platform.YouTube,
                OccurredAt = message.PublishedAt,
                UserId = message.AuthorChannelId,
                UserDisplayName = message.AuthorDisplayName,
                Bits = (int)(superChat.AmountMicros / MicrosPerMinorUnit),
                Message = superChat.UserComment,
                IsAnonymous = false,
            },

            "superStickerEvent" when message.SuperStickerDetails is { } superSticker =>
                new CheerEvent
                {
                    BroadcasterId = tenantId,
                    Provider = AuthEnums.Platform.YouTube,
                    OccurredAt = message.PublishedAt,
                    UserId = message.AuthorChannelId,
                    UserDisplayName = message.AuthorDisplayName,
                    Bits = (int)(superSticker.AmountMicros / MicrosPerMinorUnit),
                    Message = superSticker.AltText,
                    IsAnonymous = false,
                },

            "newSponsorEvent" when message.NewSponsorDetails is { } newSponsor =>
                new NewSubscriptionEvent
                {
                    BroadcasterId = tenantId,
                    Provider = AuthEnums.Platform.YouTube,
                    OccurredAt = message.PublishedAt,
                    UserId = message.AuthorChannelId,
                    UserDisplayName = message.AuthorDisplayName,
                    Tier = newSponsor.MemberLevelName,
                },

            "memberMilestoneChatEvent" when message.MemberMilestoneChatDetails is { } milestone =>
                new ResubscriptionEvent
                {
                    BroadcasterId = tenantId,
                    Provider = AuthEnums.Platform.YouTube,
                    OccurredAt = message.PublishedAt,
                    UserId = message.AuthorChannelId,
                    UserDisplayName = message.AuthorDisplayName,
                    Tier = milestone.MemberLevelName,
                    CumulativeMonths = (int)milestone.MemberMonth,
                    StreakMonths = 0, // YouTube does not report streaks — never invent one.
                    Message = string.IsNullOrEmpty(milestone.UserComment)
                        ? null
                        : milestone.UserComment,
                },

            "membershipGiftingEvent" when message.MembershipGiftingDetails is { } gifting =>
                new GiftSubscriptionEvent
                {
                    BroadcasterId = tenantId,
                    Provider = AuthEnums.Platform.YouTube,
                    OccurredAt = message.PublishedAt,
                    GifterUserId = message.AuthorChannelId,
                    GifterDisplayName = message.AuthorDisplayName,
                    Tier = gifting.GiftMembershipsLevelName,
                    GiftCount = gifting.GiftMembershipsCount,
                    IsAnonymous = false,
                    // YouTube does not enumerate recipients on the gifter's message (unlike Kick).
                    Recipients = [],
                },

            // "textMessageEvent" (plain chat) and "giftMembershipReceivedEvent" (the recipient's own line,
            // not in the §4.1 map — the gifter's membershipGiftingEvent above already covers the batch).
            _ => null,
        };
}

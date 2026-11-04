// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Chat.Events;

/// <summary>
/// Published for EventSub <c>channel.chat.notification</c> — Twitch's unified chat-notice channel covering subs,
/// resubs, gifts, raids, announcements, bits-badge tiers and more. Deliberately a single general carrier rather
/// than twenty per-notice events: <see cref="NoticeType"/> identifies which notice fired, and the human-readable
/// <see cref="SystemMessage"/> plus the chatter's own <see cref="MessageText"/> carry the content. Consumers that
/// care about one notice branch on <see cref="NoticeType"/>; the rest log/relay the system message.
/// </summary>
public sealed class ChatNotificationEvent : DomainEventBase
{
    /// <summary>The Twitch user id (a number as text) of the viewer the notice is about. Empty when the notice is anonymous.</summary>
    public required string ChatterUserId { get; init; }

    /// <summary>The name of that viewer as shown in chat. Empty when the notice is anonymous.</summary>
    public required string ChatterDisplayName { get; init; }

    /// <summary>The login name of that viewer, in lowercase. Empty when the notice is anonymous.</summary>
    public required string ChatterLogin { get; init; }

    /// <summary>True for anonymous gift notices (Twitch zeroes the chatter id/login/name).</summary>
    public required bool IsAnonymous { get; init; }

    /// <summary>
    /// The notice discriminator, e.g. <c>sub</c>, <c>resub</c>, <c>sub_gift</c>, <c>community_sub_gift</c>,
    /// <c>raid</c>, <c>announcement</c>, <c>bits_badge_tier</c>, <c>pay_it_forward</c>, <c>charity_donation</c>.
    /// </summary>
    public required string NoticeType { get; init; }

    /// <summary>Twitch's pre-rendered human-readable line for the notice (e.g. "User subscribed at Tier 1").</summary>
    public required string SystemMessage { get; init; }

    /// <summary>The chatter's own message text accompanying the notice (concatenated fragments), or empty.</summary>
    public required string MessageText { get; init; }

    /// <summary>The id of the chat message that carries the notice.</summary>
    public required string MessageId { get; init; }

    /// <summary>The color of an announcement: <c>PRIMARY</c>, <c>BLUE</c>, <c>GREEN</c>, <c>ORANGE</c> or <c>PURPLE</c>. Empty for other notices.</summary>
    public string? AnnouncementColor { get; init; }

    /// <summary>The Twitch id of the channel the notice came from in shared chat. Empty when it came from this channel.</summary>
    public string? SourceChannelId { get; init; }

    /// <summary>The name of the channel the notice came from in shared chat. Empty when it came from this channel.</summary>
    public string? SourceChannelName { get; init; }

    /// <summary>The sub tier of a sub, resub or gift notice: <c>1000</c>, <c>2000</c> or <c>3000</c>. Empty for other notices.</summary>
    public string? SubTier { get; init; }

    /// <summary>The total months the viewer has subscribed, for a resub notice. Empty for other notices.</summary>
    public int? CumulativeMonths { get; init; }

    /// <summary>The number of subs given in a community gift notice. Empty for other notices.</summary>
    public int? GiftCount { get; init; }

    /// <summary>The number of viewers a raid brought. Empty for other notices.</summary>
    public int? RaidViewerCount { get; init; }

    /// <summary>The Twitch id of the viewer who received a gifted sub. Empty for other notices.</summary>
    public string? RecipientUserId { get; init; }

    /// <summary>The name of the viewer who received a gifted sub, as shown in chat. Empty for other notices.</summary>
    public string? RecipientDisplayName { get; init; }
}

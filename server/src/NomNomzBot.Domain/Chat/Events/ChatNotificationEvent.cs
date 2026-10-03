// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
}

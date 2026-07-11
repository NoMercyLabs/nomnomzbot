// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Chat.Events;

/// <summary>
/// THE canonical chat fact — published for every chat message the bot ingests on any platform: Twitch
/// (EventSub channel.chat.message) and YouTube (Live Chat poller); <see cref="Provider"/> names the source.
/// This is the HOT PATH event — handlers must be fast, and Twitch-only consumers (command replies,
/// auto-mod enforcement, pronouns, decoration) gate on <see cref="Provider"/>.
/// </summary>
public sealed class ChatMessageReceivedEvent : DomainEventBase
{
    public required string MessageId { get; init; }

    /// <summary>
    /// The platform this message arrived on — <see cref="AuthEnums.Platform"/> key (the vocabulary shared
    /// with <c>Channel.Provider</c> / <c>UserIdentity.Provider</c>). Defaults to Twitch, the dominant source.
    /// </summary>
    public string Provider { get; init; } = AuthEnums.Platform.Twitch;

    // The tenant (channel) id is inherited from DomainEventBase as a Guid. The platform-native broadcaster
    // string id is carried alongside for the send/reply boundary (Twitch: the Helix broadcaster id, passed
    // to IChatProvider; YouTube: the channel id of the streamer's YouTube channel).
    public required string TwitchBroadcasterId { get; init; }
    public required string UserId { get; init; }
    public required string UserDisplayName { get; init; }
    public required string UserLogin { get; init; }

    /// <summary>Raw plain-text content (concatenation of all fragment texts).</summary>
    public required string Message { get; init; }

    /// <summary>
    /// Structured fragments from EventSub: text, emote, cheermote, mention.
    /// Enables inline emote rendering, colored mentions, animated cheermotes.
    /// </summary>
    public required IReadOnlyList<ChatMessageFragment> Fragments { get; init; }

    /// <summary>User's chat color as #RRGGBB hex (or null if unset).</summary>
    public string? ColorHex { get; init; }

    /// <summary>
    /// Message type from EventSub: "text" | "channel_points_highlighted" |
    /// "channel_points_sub_only" | "user_intro" | "power_ups_message_effect" |
    /// "power_ups_gigantified_emote"
    /// </summary>
    public string MessageType { get; init; } = "text";

    /// <summary>Parsed badges with their set ID, badge ID, and info field.</summary>
    public required IReadOnlyList<ChatBadge> Badges { get; init; }

    public required bool IsSubscriber { get; init; }
    public required bool IsVip { get; init; }
    public required bool IsModerator { get; init; }
    public required bool IsBroadcaster { get; init; }

    /// <summary>Bits cheered in this message, or 0.</summary>
    public int Bits { get; init; }

    /// <summary>If this is a reply, the parent message ID.</summary>
    public string? ReplyParentMessageId { get; init; }

    /// <summary>The parent reply message text (for display in the UI thread).</summary>
    public string? ReplyParentMessageBody { get; init; }

    /// <summary>Display name of the user being replied to.</summary>
    public string? ReplyParentUserName { get; init; }
}

// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Api.Hubs.Dtos;

/// <summary>
/// Rich chat message DTO sent to dashboard/overlay clients via SignalR.
/// Includes structured fragments for inline emote, mention, and cheermote rendering.
/// Field names match the frontend ChatMessagePayload type exactly.
/// </summary>
public record DashboardChatMessageDto(
    string Id,
    string ChannelId,
    string UserId,
    string DisplayName,
    string Username,
    /// <summary>Raw plain-text fallback (for clients that don't render fragments).</summary>
    string Message,
    /// <summary>Structured fragments: text, emote, cheermote, mention.</summary>
    IReadOnlyList<ChatFragmentDto> Fragments,
    /// <summary>Derived role: broadcaster | moderator | vip | subscriber | viewer</summary>
    string UserType,
    bool IsSubscriber,
    bool IsVip,
    bool IsModerator,
    bool IsBroadcaster,
    bool IsCheer,
    bool IsCommand,
    IReadOnlyList<ChatBadgeDto> Badges,
    int BitsAmount,
    /// <summary>User's chat color #RRGGBB.</summary>
    string? Color,
    /// <summary>Message type: text | channel_points_highlighted | channel_points_sub_only | user_intro</summary>
    string MessageType,
    string? ReplyToMessageId,
    string? ReplyParentMessageBody,
    string? ReplyParentUserName,
    string Timestamp
);

/// <summary>A single fragment of a chat message.</summary>
public record ChatFragmentDto(
    string Type,
    string Text,
    ChatEmoteDto? Emote,
    ChatCheermoteDto? Cheermote,
    ChatMentionDto? Mention
);

/// <summary>
/// Emote fragment data — the unified, render-ready shape covering Twitch and third-party (BTTV/FFZ/7TV) emotes
/// (chat-decoration spec §4). <c>Urls</c> is scale-keyed ("1"/"2"/"3"); <c>Provider</c> is the source network.
/// </summary>
public record ChatEmoteDto(
    string Id,
    string? SetId,
    string Format,
    string Provider,
    IReadOnlyDictionary<string, string> Urls,
    bool Animated,
    bool ZeroWidth
);

/// <summary>
/// Cheermote fragment data — the raw prefix/bits/tier plus the resolved tier image (chat-decoration spec §3.4):
/// scale-keyed <c>Urls</c>, whether animated, and the tier <c>ColorHex</c>. The image fields are null/empty until the
/// channel's cheermotes are warmed in cache.
/// </summary>
public record ChatCheermoteDto(
    string Prefix,
    int Bits,
    int Tier,
    IReadOnlyDictionary<string, string>? Urls,
    bool Animated,
    string? ColorHex
);

/// <summary>Mention fragment data (@user).</summary>
public record ChatMentionDto(string UserId, string Username, string DisplayName);

/// <summary>
/// A chat badge (subscriber, moderator, etc.) resolved to its scale-keyed image urls ("1"/"2"/"4") from the cached
/// Helix badge sets (chat-decoration spec §3.3). <c>Urls</c> is empty when the badge set is not yet warmed in cache.
/// </summary>
public record ChatBadgeDto(
    string SetId,
    string Id,
    string? Info,
    IReadOnlyDictionary<string, string> Urls
);

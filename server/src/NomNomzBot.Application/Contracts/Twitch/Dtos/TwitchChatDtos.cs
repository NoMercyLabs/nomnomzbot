// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Twitch;

// Helix "Chat" category wire models (GET/PATCH /chat/settings, GET/PUT /chat/color,
// GET/POST/PATCH/DELETE /chat/pinned_messages). These records deserialize straight from Twitch's
// snake_case JSON via the transport's naming policy — no per-property annotations. Twitch ids stay
// strings; the owning tenant is always passed in as a Guid method argument, never here. Plain Send
// Chat Message is intentionally absent (owned by HelixChatProvider).

/// <summary>
/// Get / Update Chat Settings — the broadcaster's current chat-room configuration. The optional
/// fields (<c>ModeratorId</c>, <c>NonModeratorChatDelay</c>, <c>NonModeratorChatDelayDuration</c>) are
/// only returned when the request was made with the moderator's user token, hence nullable here.
/// </summary>
public sealed record TwitchChatSettings(
    string BroadcasterId,
    bool EmoteMode,
    bool FollowerMode,
    int? FollowerModeDuration,
    string? ModeratorId,
    bool? NonModeratorChatDelay,
    int? NonModeratorChatDelayDuration,
    bool SlowMode,
    int? SlowModeWaitTime,
    bool SubscriberMode,
    bool UniqueChatMode
);

/// <summary>The color used for a user's name in chat (Get User Chat Color).</summary>
public sealed record TwitchUserChatColor(
    string UserId,
    string UserLogin,
    string UserName,
    string Color
);

/// <summary>The broadcaster's currently pinned chat message (Get Pinned Chat Message).</summary>
public sealed record TwitchPinnedChatMessage(
    string MessageId,
    string Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    bool IsPinnedByBroadcaster,
    bool IsPinnedByModerator
);

/// <summary>
/// Update Chat Settings request body. All fields optional — only the ones set are sent (the transport
/// omits nulls), matching Twitch's "patch only what you provide" semantics. The broadcaster and
/// moderator are query parameters resolved from the Guid method argument, not part of this body.
/// </summary>
public sealed record UpdateChatSettingsRequest(
    bool? EmoteMode = null,
    bool? FollowerMode = null,
    int? FollowerModeDuration = null,
    bool? NonModeratorChatDelay = null,
    int? NonModeratorChatDelayDuration = null,
    bool? SlowMode = null,
    int? SlowModeWaitTime = null,
    bool? SubscriberMode = null,
    bool? UniqueChatMode = null
);

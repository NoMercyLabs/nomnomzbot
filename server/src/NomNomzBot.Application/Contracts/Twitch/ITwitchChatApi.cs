// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// The Helix "Chat" category sub-client: announcements, shoutouts, chat settings, name color, and the
/// pinned-message lifecycle (twitch-helix.md §3.2). One of the grouped sub-clients exposed by
/// <see cref="ITwitchHelixClient"/>. Every method takes the owning tenant as a <see cref="Guid"/> and
/// resolves it to the Twitch id internally (the invariant: a Guid never reaches Twitch). Each returns
/// <see cref="Result"/>/<see cref="Result{T}"/> carrying a closed <see cref="TwitchErrorCodes"/> on failure.
///
/// Plain "Send Chat Message" is intentionally absent — it is owned by the existing chat-send provider.
/// The moderator-scoped endpoints here send the tenant's own Twitch id for both <c>broadcaster_id</c> and
/// <c>moderator_id</c> (the tenant moderates their own channel with their own token).
/// </summary>
public interface ITwitchChatApi
{
    /// <summary>Send Chat Announcement — posts an announcement, optionally tinted. Requires <c>moderator:manage:announcements</c>.</summary>
    Task<Result> SendAnnouncementAsync(
        Guid broadcasterId,
        string message,
        string? color,
        CancellationToken ct = default
    );

    /// <summary>Send a Shoutout — shouts out another channel (raw Twitch id). Requires <c>moderator:manage:shoutouts</c>.</summary>
    Task<Result> SendShoutoutAsync(
        Guid broadcasterId,
        string toTwitchBroadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Get Chat Settings — current emote / follower / slow / subscriber / unique-chat configuration. User token; no scope.</summary>
    Task<Result<TwitchChatSettings>> GetChatSettingsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Update Chat Settings — patches only the supplied fields. Requires <c>moderator:manage:chat_settings</c>.</summary>
    Task<Result<TwitchChatSettings>> UpdateChatSettingsAsync(
        Guid broadcasterId,
        UpdateChatSettingsRequest request,
        CancellationToken ct = default
    );

    /// <summary>Get User Chat Color — the tenant's own name color in chat. User token; no scope.</summary>
    Task<Result<TwitchUserChatColor>> GetUserChatColorAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Update User Chat Color — sets the tenant's name color (named color or hex). Requires <c>user:manage:chat_color</c>.</summary>
    Task<Result> UpdateUserChatColorAsync(
        Guid broadcasterId,
        string color,
        CancellationToken ct = default
    );

    /// <summary>Get Pinned Chat Message — the channel's currently pinned message, or <c>not_found</c> when none. User token; no scope.</summary>
    Task<Result<TwitchPinnedChatMessage>> GetPinnedMessagesAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Pin Chat Message — pins a message for an optional duration. Requires <c>moderator:manage:chat_messages</c>.</summary>
    Task<Result<TwitchPinnedChatMessage>> PinMessageAsync(
        Guid broadcasterId,
        string messageId,
        int? durationSeconds,
        CancellationToken ct = default
    );

    /// <summary>Update Pinned Chat Message — changes the pinned message's remaining duration. Requires <c>moderator:manage:chat_messages</c>.</summary>
    Task<Result<TwitchPinnedChatMessage>> UpdatePinnedMessageAsync(
        Guid broadcasterId,
        int? durationSeconds,
        CancellationToken ct = default
    );

    /// <summary>Unpin Chat Message — removes the channel's pinned message. Requires <c>moderator:manage:chat_messages</c>.</summary>
    Task<Result> UnpinMessageAsync(Guid broadcasterId, CancellationToken ct = default);
}

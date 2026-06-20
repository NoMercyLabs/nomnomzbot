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
/// The Helix "Chat assets" category sub-client: the read-only chat assets — the live chatter list, channel
/// and global emotes, emote sets, the user's cross-channel emotes, channel and global chat badges, and the
/// active shared chat session (twitch-helix.md §3.2). One of the grouped sub-clients exposed by
/// <see cref="ITwitchHelixClient"/>. Every method takes the owning tenant as a <see cref="Guid"/> and
/// resolves it to the Twitch id internally (the invariant: a Guid never reaches Twitch). Each returns
/// <see cref="Result"/>/<see cref="Result{T}"/> carrying a closed <see cref="TwitchErrorCodes"/> on failure.
/// </summary>
public interface ITwitchChatAssetsApi
{
    /// <summary>
    /// Get Chatters — one page of the viewers currently connected to the broadcaster's chat. The moderator
    /// is the tenant itself (resolved internally). Requires <c>moderator:read:chatters</c>.
    /// </summary>
    Task<Result<TwitchPage<TwitchChatter>>> GetChattersAsync(
        Guid broadcasterId,
        TwitchPageRequest page,
        CancellationToken ct = default
    );

    /// <summary>Get Channel Emotes — the broadcaster's custom (sub / follower / bits-tier) emotes. App token; no scope.</summary>
    Task<Result<IReadOnlyList<TwitchChannelEmote>>> GetChannelEmotesAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Get Global Emotes — Twitch's global emotes, available in every channel. App token; no scope; no params.</summary>
    Task<Result<IReadOnlyList<TwitchGlobalEmote>>> GetGlobalEmotesAsync(
        CancellationToken ct = default
    );

    /// <summary>Get Emote Sets — the emotes in one or more emote sets (repeated <c>emote_set_id</c>). App token; no scope.</summary>
    Task<Result<IReadOnlyList<TwitchEmoteSetEmote>>> GetEmoteSetsAsync(
        IReadOnlyList<string> emoteSetIds,
        CancellationToken ct = default
    );

    /// <summary>
    /// Get User Emotes — one page of the emotes available to the tenant across all channels (the user id is
    /// the resolved tenant). Cursor-paged with no <c>total</c>. Requires <c>user:read:emotes</c>.
    /// </summary>
    Task<Result<TwitchPage<TwitchUserEmote>>> GetUserEmotesAsync(
        Guid broadcasterId,
        string? afterCursor,
        CancellationToken ct = default
    );

    /// <summary>Get Channel Chat Badges — the broadcaster's custom chat-badge sets. App token; no scope.</summary>
    Task<Result<IReadOnlyList<TwitchChatBadgeSet>>> GetChannelChatBadgesAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Get Global Chat Badges — Twitch's global chat-badge sets, usable in any channel. App token; no scope; no params.</summary>
    Task<Result<IReadOnlyList<TwitchChatBadgeSet>>> GetGlobalChatBadgesAsync(
        CancellationToken ct = default
    );

    /// <summary>
    /// Get Shared Chat Session — the channel's active shared chat session, or <c>not_found</c> when the
    /// channel is not in one (empty <c>data[]</c>). App token; no scope.
    /// </summary>
    Task<Result<TwitchSharedChatSession>> GetSharedChatSessionAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );
}

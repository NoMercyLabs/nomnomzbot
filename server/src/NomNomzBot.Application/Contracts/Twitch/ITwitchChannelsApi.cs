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
/// The Helix "Channels" category sub-client: channel info, editors, and the follow relationships
/// (twitch-helix.md §3.2). One of the grouped sub-clients exposed by <see cref="ITwitchHelixClient"/>.
/// Every method takes the owning tenant as a <see cref="Guid"/> and resolves it to the Twitch id internally
/// (the invariant: a Guid never reaches Twitch). Each returns <see cref="Result"/>/<see cref="Result{T}"/>
/// carrying a closed <see cref="TwitchErrorCodes"/> on failure.
/// </summary>
public interface ITwitchChannelsApi
{
    /// <summary>Get Channel Information — current title, category, language, tags, content labels. App token; no scope.</summary>
    Task<Result<TwitchChannelInformation>> GetChannelInformationAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Modify Channel Information — title / category / language / tags / CCLs / branded-content. Requires <c>channel:manage:broadcast</c>.</summary>
    Task<Result> ModifyChannelInformationAsync(
        Guid broadcasterId,
        ModifyChannelInformationRequest request,
        CancellationToken ct = default
    );

    /// <summary>Get Channel Editors — users with editor access. Requires <c>channel:read:editors</c>.</summary>
    Task<Result<IReadOnlyList<TwitchChannelEditor>>> GetChannelEditorsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Get Followed Channels — one page of channels the tenant follows, newest first. Optionally filtered to a
    /// single target channel (raw Twitch id) to check a specific follow. Requires <c>user:read:follows</c>.
    /// </summary>
    Task<Result<TwitchPage<TwitchFollowedChannel>>> GetFollowedChannelsAsync(
        Guid broadcasterId,
        string? filterTwitchBroadcasterId,
        TwitchPageRequest page,
        CancellationToken ct = default
    );

    /// <summary>Get Channel Followers — one page of the channel's followers. Requires <c>moderator:read:followers</c>.</summary>
    Task<Result<TwitchPage<TwitchChannelFollower>>> GetChannelFollowersAsync(
        Guid broadcasterId,
        TwitchPageRequest page,
        CancellationToken ct = default
    );

    /// <summary>Get the total follower count (<c>?first=1</c>, reads <c>total</c>). Requires <c>moderator:read:followers</c>.</summary>
    Task<Result<int>> GetChannelFollowerCountAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Get a SINGLE user's follow record (<c>channels/followers?user_id=</c>) — the follow date for the
    /// <c>{{user.followAge}}</c>/<c>{{target.followAge}}</c> template helpers. Returns null when that user does
    /// not follow the channel. Requires <c>moderator:read:followers</c>.
    /// </summary>
    Task<Result<TwitchChannelFollower?>> GetChannelFollowerAsync(
        Guid broadcasterId,
        string userTwitchId,
        CancellationToken ct = default
    );
}

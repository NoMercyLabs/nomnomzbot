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
/// The Helix "Videos" category sub-client: list a broadcaster's videos, fetch videos by id, and delete
/// videos (twitch-helix.md §3.2). One of the grouped sub-clients exposed by <see cref="ITwitchHelixClient"/>.
/// Every method takes the owning tenant as a <see cref="Guid"/> and resolves it to the Twitch id internally
/// (the invariant: a Guid never reaches Twitch). Each returns <see cref="Result"/>/<see cref="Result{T}"/>
/// carrying a closed <see cref="TwitchErrorCodes"/> on failure.
/// </summary>
public interface ITwitchVideosApi
{
    /// <summary>
    /// Get Videos — one page of the broadcaster's published videos (resolved tenant as <c>user_id</c>),
    /// optionally narrowed by <paramref name="type"/> (<c>all|archive|highlight|upload</c>),
    /// <paramref name="period"/> (<c>all|day|week|month</c>), and <paramref name="sort"/>
    /// (<c>time|trending|views</c>). App token; no scope.
    /// </summary>
    Task<Result<TwitchPage<TwitchVideo>>> GetVideosByBroadcasterAsync(
        Guid broadcasterId,
        string? type,
        string? period,
        string? sort,
        TwitchPageRequest page,
        CancellationToken ct = default
    );

    /// <summary>Get Videos — fetch specific videos by their (raw Twitch) ids. App token; no scope.</summary>
    Task<Result<IReadOnlyList<TwitchVideo>>> GetVideosByIdsAsync(
        IReadOnlyList<string> videoIds,
        CancellationToken ct = default
    );

    /// <summary>
    /// Delete Videos — delete up to five of the broadcaster's own videos (past broadcasts, highlights, or
    /// uploads), returning the ids Twitch confirmed deleted. Requires <c>channel:manage:videos</c>.
    /// </summary>
    Task<Result<IReadOnlyList<string>>> DeleteVideosAsync(
        Guid broadcasterId,
        IReadOnlyList<string> videoIds,
        CancellationToken ct = default
    );
}

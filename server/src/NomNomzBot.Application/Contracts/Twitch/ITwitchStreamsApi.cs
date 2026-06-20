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
/// The Helix "Streams" category sub-client: live-stream listing, the channel's stream key, the tenant's
/// followed live streams, and stream markers (twitch-helix.md §3.2). One of the grouped sub-clients exposed
/// by <see cref="ITwitchHelixClient"/>. Tenant-scoped methods take the owning tenant as a <see cref="Guid"/>
/// and resolve it to the Twitch id internally (the invariant: a Guid never reaches Twitch). Each returns
/// <see cref="Result"/>/<see cref="Result{T}"/> carrying a closed <see cref="TwitchErrorCodes"/> on failure.
/// </summary>
public interface ITwitchStreamsApi
{
    /// <summary>
    /// Get Streams — one page of live streams, newest-watched first, filtered by any combination of user ids,
    /// logins, game ids, languages and type. App token; no scope (a subject-agnostic public read).
    /// </summary>
    Task<Result<TwitchPage<TwitchStream>>> GetStreamsAsync(
        TwitchStreamsFilter filter,
        TwitchPageRequest page,
        CancellationToken ct = default
    );

    /// <summary>
    /// Get the tenant's own stream — resolves the channel and filters Get Streams by <c>user_id</c>. App token;
    /// no scope. An empty <c>data[]</c> means the channel is offline, surfaced as <c>not_found</c>.
    /// </summary>
    Task<Result<TwitchStream>> GetStreamAsync(Guid broadcasterId, CancellationToken ct = default);

    /// <summary>Get Stream Key — the channel's RTMP stream key string. Requires <c>channel:read:stream_key</c>.</summary>
    Task<Result<string>> GetStreamKeyAsync(Guid broadcasterId, CancellationToken ct = default);

    /// <summary>
    /// Get Followed Streams — one page of the live streams the tenant follows. Requires <c>user:read:follows</c>.
    /// </summary>
    Task<Result<TwitchPage<TwitchStream>>> GetFollowedStreamsAsync(
        Guid broadcasterId,
        TwitchPageRequest page,
        CancellationToken ct = default
    );

    /// <summary>
    /// Create Stream Marker — marks the current position of the tenant's live stream, optionally with a
    /// description, and returns the created marker. Requires <c>channel:manage:broadcast</c>.
    /// </summary>
    Task<Result<TwitchStreamMarker>> CreateStreamMarkerAsync(
        Guid broadcasterId,
        string? description = null,
        CancellationToken ct = default
    );

    /// <summary>
    /// Get Stream Markers — one page of markers from the tenant's most recent stream, or from a specific VOD
    /// when <paramref name="videoId"/> is supplied. The response nests markers under videos, so each page item
    /// is a <see cref="TwitchStreamMarkerGroup"/> (<c>user → videos[] → markers[]</c>). Requires <c>user:read:broadcast</c>.
    /// </summary>
    Task<Result<TwitchPage<TwitchStreamMarkerGroup>>> GetStreamMarkersAsync(
        Guid broadcasterId,
        string? videoId,
        TwitchPageRequest page,
        CancellationToken ct = default
    );
}

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
/// The Helix "Clips" category sub-client: create a clip from the live stream, create a clip from a VOD, and
/// read clips back by broadcaster or by id (twitch-helix.md §3). One of the grouped sub-clients exposed by
/// <see cref="ITwitchHelixClient"/>. Tenant-scoped methods take the owning tenant as a <see cref="Guid"/> and
/// resolve it to the Twitch id internally (the invariant: a Guid never reaches Twitch); the id-keyed read is
/// a public App-token lookup with no tenant. Each returns <see cref="Result"/>/<see cref="Result{T}"/>
/// carrying a closed <see cref="TwitchErrorCodes"/> on failure.
/// </summary>
public interface ITwitchClipsApi
{
    /// <summary>
    /// Create Clip — captures a clip from the broadcaster's active stream and returns its id + edit URL.
    /// <paramref name="hasDelay"/> adds the broadcaster's stream delay before capturing. Requires <c>clips:edit</c>.
    /// </summary>
    Task<Result<TwitchClipStub>> CreateClipAsync(
        Guid broadcasterId,
        bool? hasDelay,
        CancellationToken ct = default
    );

    /// <summary>
    /// Create Clip From VOD — captures a clip from one of the broadcaster's past VODs at a given offset, on
    /// behalf of the broadcaster or one of their editors. Requires <c>editor:manage:clips</c> (the broadcaster
    /// may alternatively hold <c>channel:manage:clips</c>).
    /// </summary>
    Task<Result<TwitchClipStub>> CreateClipFromVodAsync(
        Guid broadcasterId,
        CreateClipFromVodRequest request,
        CancellationToken ct = default
    );

    /// <summary>Get Clips — one page of the broadcaster's clips, newest first. App token; no scope.</summary>
    Task<Result<TwitchPage<TwitchClip>>> GetClipsByBroadcasterAsync(
        Guid broadcasterId,
        TwitchPageRequest page,
        CancellationToken ct = default
    );

    /// <summary>
    /// Get Clips — looks up specific clips by their ids (up to 100, one repeated <c>id</c> query param each).
    /// App token; no scope; no tenant (clip ids are global).
    /// </summary>
    Task<Result<IReadOnlyList<TwitchClip>>> GetClipsByIdsAsync(
        IReadOnlyList<string> clipIds,
        CancellationToken ct = default
    );

    /// <summary>
    /// Get Clips Download — temporary download URLs for the broadcaster's clips (up to 10, one repeated
    /// <c>clip_id</c> query param each). <paramref name="editorId"/> is the Twitch user id of the
    /// broadcaster or editor downloading (must match the user token). Requires <c>editor:manage:clips</c>
    /// (the broadcaster may alternatively hold <c>channel:manage:clips</c>).
    /// </summary>
    Task<Result<IReadOnlyList<TwitchClipDownload>>> GetClipDownloadUrlsAsync(
        Guid broadcasterId,
        string editorId,
        IReadOnlyList<string> clipIds,
        CancellationToken ct = default
    );
}

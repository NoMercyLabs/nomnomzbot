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

namespace NomNomzBot.Application.Contracts.Music;

/// <summary>
/// The per-user manage surface (music-sr.md §3.10) — playlist CRUD, saved-tracks/library,
/// follow/unfollow, and ratings/subscriptions against the <b>broadcaster's own</b> provider account.
/// Not the SR queue and not transport playback. One generic shape across providers; the provider's
/// capabilities decide which calls are supported (<c>Library</c>/<c>Playlists</c>/<c>Subscriptions</c>),
/// gated by flag — no name checks. An unsupported call fails <c>CAPABILITY_UNSUPPORTED</c>; an
/// unconnected/insufficiently-scoped provider fails <c>MISSING_SCOPE</c>; an unregistered provider
/// key fails <c>NOT_FOUND</c>.
/// </summary>
public interface IMusicProviderManageApi
{
    // ── Playlists (capability: Playlists) ──────────────────────────────────────

    /// <summary>Lists the user's playlists on the provider. Read.</summary>
    Task<Result<IReadOnlyList<MusicPlaylistDto>>> ListPlaylistsAsync(
        Guid broadcasterId,
        string provider,
        CancellationToken cancellationToken = default
    );

    /// <summary>Creates a playlist (Spotify: POST /users/{id}/playlists; YouTube: playlists.insert).</summary>
    Task<Result<MusicPlaylistDto>> CreatePlaylistAsync(
        Guid broadcasterId,
        string provider,
        CreateMusicPlaylistDto request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Renames / re-describes / sets visibility. <c>NOT_FOUND</c> if the playlist is not the user's.</summary>
    Task<Result<MusicPlaylistDto>> UpdatePlaylistAsync(
        Guid broadcasterId,
        string provider,
        string playlistId,
        UpdateMusicPlaylistDto request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Spotify: unfollow-own-playlist (no hard delete); YouTube: playlists.delete.</summary>
    Task<Result> DeletePlaylistAsync(
        Guid broadcasterId,
        string provider,
        string playlistId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Appends tracks to a playlist.</summary>
    Task<Result> AddPlaylistTracksAsync(
        Guid broadcasterId,
        string provider,
        string playlistId,
        IReadOnlyList<string> trackUris,
        CancellationToken cancellationToken = default
    );

    /// <summary>Removes tracks from a playlist.</summary>
    Task<Result> RemovePlaylistTracksAsync(
        Guid broadcasterId,
        string provider,
        string playlistId,
        IReadOnlyList<string> trackUris,
        CancellationToken cancellationToken = default
    );

    // ── Library / saved tracks + ratings (capability: Library) ─────────────────

    /// <summary>Spotify: PUT /me/tracks (save to Liked Songs); YouTube: videos.rate(rating="like").</summary>
    Task<Result> SaveTracksAsync(
        Guid broadcasterId,
        string provider,
        IReadOnlyList<string> trackUris,
        CancellationToken cancellationToken = default
    );

    /// <summary>Spotify: DELETE /me/tracks; YouTube: videos.rate(rating="none").</summary>
    Task<Result> RemoveSavedTracksAsync(
        Guid broadcasterId,
        string provider,
        IReadOnlyList<string> trackUris,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// YouTube videos.rate(like|dislike|none). On Spotify, like/none map to save/remove;
    /// dislike fails <c>CAPABILITY_UNSUPPORTED</c>.
    /// </summary>
    Task<Result> RateTrackAsync(
        Guid broadcasterId,
        string provider,
        string trackUri,
        MusicRating rating,
        CancellationToken cancellationToken = default
    );

    // ── Follow / unfollow (capability resolved by target kind: channel → Subscriptions, artist/playlist → Library) ──

    /// <summary>Spotify: PUT /me/following (artist) or follow-playlist; YouTube: subscriptions.insert (channel).</summary>
    Task<Result> FollowAsync(
        Guid broadcasterId,
        string provider,
        MusicFollowTarget target,
        string targetId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Inverse of <see cref="FollowAsync"/> (Spotify DELETE /me/following / unfollow-playlist; YouTube subscriptions.delete).</summary>
    Task<Result> UnfollowAsync(
        Guid broadcasterId,
        string provider,
        MusicFollowTarget target,
        string targetId,
        CancellationToken cancellationToken = default
    );
}

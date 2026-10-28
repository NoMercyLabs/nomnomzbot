// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Dtos;

namespace NomNomzBot.Application.Music.Services;

/// <summary>
/// A channel's blocked song-request tracks (the legacy <c>!bansong</c> list). Blocks are matched by
/// track URI on the song-request admission path (<see cref="IMusicService.AddToQueueAsync"/>), which
/// refuses a blocked track with <c>TRACK_BLOCKED</c> before it reaches the fair queue — the one
/// enforcement point every SR flow (command, reward pipeline, public SR page, script) goes through.
/// </summary>
public interface IBlockedTrackService
{
    /// <summary>
    /// Blocks a track for the channel. Idempotent: re-blocking an already-blocked URI returns the
    /// existing row. Fails <c>VALIDATION_FAILED</c> on a blank provider, URI, or title.
    /// </summary>
    Task<Result<BlockedTrackDto>> BlockAsync(
        Guid broadcasterId,
        BlockTrackRequest request,
        CancellationToken ct = default
    );

    /// <summary>Unblocks (soft-deletes) a blocked track by id. <c>NOT_FOUND</c> if absent.</summary>
    Task<Result> UnblockAsync(
        Guid broadcasterId,
        Guid blockedTrackId,
        CancellationToken ct = default
    );

    /// <summary>Lists the channel's blocked tracks, newest first, paged.</summary>
    Task<Result<PagedList<BlockedTrackDto>>> ListAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    );

    /// <summary>Whether the given track URI is blocked for the channel (any provider).</summary>
    Task<bool> IsBlockedAsync(Guid broadcasterId, string trackUri, CancellationToken ct = default);

    /// <summary>
    /// How many of the channel's live blocks this person banned (the legacy <c>!bansong</c> strike count).
    /// Unblocking a track takes its strike away.
    /// </summary>
    Task<int> CountByBlockerAsync(
        Guid broadcasterId,
        string blockedByUserId,
        CancellationToken ct = default
    );
}

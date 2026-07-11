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
using NomNomzBot.Application.MediaShare.Dtos;

namespace NomNomzBot.Application.MediaShare.Services;

/// <summary>
/// The viewer clip/video queue (media-share.md §3). Safe-by-default: a closed source set, a hard duration
/// cap, and pre-play mod approval on unless the streamer turns it off. Distinct from music song-requests —
/// this queues short VIDEO clips that play on an overlay.
/// </summary>
public interface IMediaShareService
{
    /// <summary>
    /// Validate the URL → source (D2), fetch metadata, enforce the duration cap (D3), eligibility +
    /// per-user cooldown + queue length (D4/D5), debit the entry cost when set, and enqueue (pending, or
    /// approved when approval is off). Fails closed with a stable code.
    /// </summary>
    Task<Result<MediaShareRequestDto>> SubmitAsync(
        Guid broadcasterId,
        Guid requesterUserId,
        SubmitMediaRequest request,
        CancellationToken ct = default
    );

    /// <summary>Approve a pending item → approved, appended to the play order.</summary>
    Task<Result<MediaShareRequestDto>> ApproveAsync(
        Guid broadcasterId,
        Guid requestId,
        Guid moderatorUserId,
        CancellationToken ct = default
    );

    /// <summary>Reject an item → rejected (refunds the entry cost if it was charged).</summary>
    Task<Result> RejectAsync(
        Guid broadcasterId,
        Guid requestId,
        Guid moderatorUserId,
        CancellationToken ct = default
    );

    /// <summary>Skip a playing/approved item → skipped (refunds the entry cost if it was charged).</summary>
    Task<Result> SkipAsync(Guid broadcasterId, Guid requestId, CancellationToken ct = default);

    /// <summary>Move an approved item to a new 1-based play position.</summary>
    Task<Result> ReorderAsync(
        Guid broadcasterId,
        Guid requestId,
        int newPosition,
        CancellationToken ct = default
    );

    Task<Result<PagedList<MediaShareRequestDto>>> GetQueueAsync(
        Guid broadcasterId,
        MediaShareFilter filter,
        PaginationParams pagination,
        CancellationToken ct = default
    );

    /// <summary>The overlay pulls the next approved item and flips it to playing.</summary>
    Task<Result<MediaShareRequestDto?>> GetNextAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>The overlay reports completion → played, and the queue advances.</summary>
    Task<Result> MarkPlayedAsync(
        Guid broadcasterId,
        Guid requestId,
        CancellationToken ct = default
    );

    Task<Result<MediaShareConfigDto>> GetConfigAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    Task<Result<MediaShareConfigDto>> UpdateConfigAsync(
        Guid broadcasterId,
        UpdateMediaShareConfigRequest request,
        CancellationToken ct = default
    );
}

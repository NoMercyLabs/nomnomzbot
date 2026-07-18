// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Entities;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// CRUD for a channel's blocked song-request tracks (the legacy <c>!bansong</c> list). The block is a
/// per-tenant URI match; <see cref="MusicService.AddToQueueAsync"/> consults <see cref="IsBlockedAsync"/>
/// on every admission. Unblocking soft-deletes (SoftDeleteInterceptor); the filtered unique index only
/// covers live rows, so a track can be re-blocked after an unblock.
/// </summary>
public sealed class BlockedTrackService(IApplicationDbContext db) : IBlockedTrackService
{
    public async Task<Result<BlockedTrackDto>> BlockAsync(
        Guid broadcasterId,
        BlockTrackRequest request,
        CancellationToken ct = default
    )
    {
        if (
            string.IsNullOrWhiteSpace(request.Provider)
            || string.IsNullOrWhiteSpace(request.TrackUri)
            || string.IsNullOrWhiteSpace(request.Title)
        )
            return Result.Failure<BlockedTrackDto>(
                "A block needs a provider, a track URI, and a title.",
                "VALIDATION_FAILED"
            );

        // Idempotent: an existing live block for this URI is the answer, never a duplicate insert
        // (the filtered unique index would 500 on one).
        BlockedTrack? existing = await db.BlockedTracks.FirstOrDefaultAsync(
            b =>
                b.BroadcasterId == broadcasterId
                && b.Provider == request.Provider
                && b.TrackUri == request.TrackUri,
            ct
        );
        if (existing is not null)
            return Result.Success(ToDto(existing));

        BlockedTrack block = new()
        {
            BroadcasterId = broadcasterId,
            Provider = request.Provider,
            TrackUri = request.TrackUri,
            Title = request.Title,
            Reason = request.Reason,
            BlockedByUserId = request.BlockedByUserId,
        };
        db.BlockedTracks.Add(block);
        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(block));
    }

    public async Task<Result> UnblockAsync(
        Guid broadcasterId,
        Guid blockedTrackId,
        CancellationToken ct = default
    )
    {
        BlockedTrack? block = await db.BlockedTracks.FirstOrDefaultAsync(
            b => b.BroadcasterId == broadcasterId && b.Id == blockedTrackId,
            ct
        );
        if (block is null)
            return Result.Failure("Blocked track not found.", "NOT_FOUND");

        db.BlockedTracks.Remove(block); // soft-deleted by SoftDeleteInterceptor
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<PagedList<BlockedTrackDto>>> ListAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<BlockedTrack> query = db
            .BlockedTracks.Where(b => b.BroadcasterId == broadcasterId)
            .OrderByDescending(b => b.CreatedAt);

        int total = await query.CountAsync(ct);
        List<BlockedTrack> page = await query
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        List<BlockedTrackDto> items = page.Select(ToDto).ToList();
        return Result.Success(
            new PagedList<BlockedTrackDto>(items, pagination.Page, pagination.PageSize, total)
        );
    }

    public Task<bool> IsBlockedAsync(
        Guid broadcasterId,
        string trackUri,
        CancellationToken ct = default
    ) =>
        db.BlockedTracks.AnyAsync(
            b => b.BroadcasterId == broadcasterId && b.TrackUri == trackUri,
            ct
        );

    private static BlockedTrackDto ToDto(BlockedTrack b) =>
        new(b.Id, b.Provider, b.TrackUri, b.Title, b.Reason, b.BlockedByUserId, b.CreatedAt);
}

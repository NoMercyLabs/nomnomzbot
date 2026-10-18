// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Services;
using NomNomzBot.Domain.Stream.Entities;

namespace NomNomzBot.Infrastructure.Dashboard.Stats;

/// <summary>
/// Keeps every stream's recap. The live path (the <c>show_raid_recap</c> action) and the past-stream build both go
/// through <see cref="BuildAsync"/>, so a recap built later from the journal has the same shape as a live one.
/// </summary>
public sealed class StreamRecapService(
    IApplicationDbContext db,
    IStreamStatsService stats,
    TimeProvider timeProvider
) : IStreamRecapService
{
    public async Task<Result<StreamRecapDto>> BuildAsync(
        Guid broadcasterId,
        string? streamId,
        IReadOnlyCollection<string>? metrics,
        CancellationToken cancellationToken = default
    )
    {
        Result<StreamStatsDto> built = await stats.GetAsync(
            broadcasterId,
            streamId,
            metrics,
            cancellationToken
        );
        if (built.IsFailure)
            return Result.Failure<StreamRecapDto>(built.ErrorMessage, built.ErrorCode);

        StreamStatsDto data = built.Value;
        DateTime fromAt = data.Stream.StartedAt.UtcDateTime;
        DateTime toAt = (data.Stream.EndedAt ?? timeProvider.GetUtcNow()).UtcDateTime;

        StreamRecap? row = await db.StreamRecaps.FirstOrDefaultAsync(
            r => r.ChannelId == broadcasterId && r.StreamId == data.Stream.Id,
            cancellationToken
        );
        if (row is null)
        {
            row = new() { ChannelId = broadcasterId, StreamId = data.Stream.Id };
            db.StreamRecaps.Add(row);
        }

        row.FromAt = fromAt;
        row.ToAt = toAt;
        row.DataJson = JsonSerializer.Serialize(data);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(row, data));
    }

    public async Task<Result<PagedList<StreamRecapDto>>> ListAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    )
    {
        IQueryable<StreamRecap> query = db
            .StreamRecaps.AsNoTracking()
            .Where(r => r.ChannelId == broadcasterId);

        int total = await query.CountAsync(cancellationToken);
        List<StreamRecap> rows = await query
            .OrderByDescending(r => r.FromAt)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(cancellationToken);

        List<StreamRecapDto> items = rows.Select(r => ToDto(r, Read(r))).ToList();
        return Result.Success(
            new PagedList<StreamRecapDto>(items, pagination.Page, pagination.PageSize, total)
        );
    }

    public async Task<Result<StreamRecapDto>> GetAsync(
        Guid broadcasterId,
        string streamId,
        CancellationToken cancellationToken = default
    )
    {
        StreamRecap? row = await db
            .StreamRecaps.AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.ChannelId == broadcasterId && r.StreamId == streamId,
                cancellationToken
            );

        return row is null
            ? Result.Failure<StreamRecapDto>("No recap was kept for this stream.", "NOT_FOUND")
            : Result.Success(ToDto(row, Read(row)));
    }

    private static StreamStatsDto Read(StreamRecap row) =>
        JsonSerializer.Deserialize<StreamStatsDto>(row.DataJson)!;

    private static StreamRecapDto ToDto(StreamRecap row, StreamStatsDto data) =>
        new()
        {
            StreamId = row.StreamId,
            FromAt = Utc(row.FromAt),
            ToAt = Utc(row.ToAt),
            UpdatedAt = Utc(row.UpdatedAt),
            Stats = data,
        };

    private static DateTimeOffset Utc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}

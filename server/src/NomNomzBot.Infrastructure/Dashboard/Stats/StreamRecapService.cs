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
    TimeProvider timeProvider,
    IStreamOutputResolutionService? output = null
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

        // Only a live stream is still on the OBS output the viewers get; an ended one never reads the present OBS.
        StreamOutputResolution? resolution =
            output is not null && data.Stream.EndedAt is null
                ? await output.ReadAsync(broadcasterId, cancellationToken)
                : null;
        if (resolution is not null)
        {
            row.OutputWidth = resolution.Width;
            row.OutputHeight = resolution.Height;
        }

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
            OutputWidth = row.OutputWidth,
            OutputHeight = row.OutputHeight,
            Stats = data,
        };

    private static DateTimeOffset Utc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}

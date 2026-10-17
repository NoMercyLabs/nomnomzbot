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
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Services;
using NomNomzBot.Application.Dashboard.Stats;

namespace NomNomzBot.Infrastructure.Dashboard.Stats;

/// <summary>
/// Finds the stream window and runs the requested <see cref="IStreamMetric"/> classes over it, one after the
/// other (they share one database context). A metric the caller did not ask for is never computed.
/// </summary>
public sealed class StreamStatsService(
    IApplicationDbContext db,
    TimeProvider timeProvider,
    IEnumerable<IStreamMetric> metrics
) : IStreamStatsService
{
    public async Task<Result<StreamStatsDto>> GetAsync(
        Guid broadcasterId,
        string? streamId,
        IReadOnlyCollection<string>? requested,
        CancellationToken cancellationToken = default
    )
    {
        List<IStreamMetric> all = metrics.ToList();
        Result<List<IStreamMetric>> selected = Select(all, requested);
        if (selected.IsFailure)
            return Result.Failure<StreamStatsDto>(selected.ErrorMessage, selected.ErrorCode);

        Domain.Stream.Entities.Stream? stream = await FindStreamAsync(
            broadcasterId,
            streamId,
            cancellationToken
        );
        if (stream?.StartedAt is not { } startedAt)
            return Result.Failure<StreamStatsDto>("Stream not found.", "NOT_FOUND");

        StreamWindow window = new(
            broadcasterId,
            stream.Id,
            startedAt,
            stream.EndedAt,
            stream.EndedAt ?? timeProvider.GetUtcNow(),
            stream.PeakViewers,
            stream.Title,
            stream.GameName
        );
        StreamStatsDto stats = new()
        {
            Stream = new()
            {
                Id = stream.Id,
                StartedAt = startedAt,
                EndedAt = stream.EndedAt,
                Title = stream.Title,
                Game = stream.GameName,
            },
        };

        foreach (IStreamMetric metric in selected.Value)
            await metric.ComputeIntoAsync(window, stats, cancellationToken);

        return Result.Success(stats);
    }

    private static Result<List<IStreamMetric>> Select(
        List<IStreamMetric> all,
        IReadOnlyCollection<string>? requested
    )
    {
        if (requested is null || requested.Count == 0)
            return Result.Success(all);

        List<IStreamMetric> picked = [];
        foreach (string key in requested.Distinct(StringComparer.Ordinal))
        {
            IStreamMetric? metric = all.FirstOrDefault(m => m.Key == key);
            if (metric is null)
            {
                string known = string.Join(", ", all.Select(m => m.Key).Order());
                return Result.Failure<List<IStreamMetric>>(
                    $"Unknown stream metric '{key}'. Known metrics: {known}.",
                    "VALIDATION_FAILED"
                );
            }

            picked.Add(metric);
        }

        return Result.Success(picked);
    }

    private async Task<Domain.Stream.Entities.Stream?> FindStreamAsync(
        Guid broadcasterId,
        string? streamId,
        CancellationToken cancellationToken
    )
    {
        IQueryable<Domain.Stream.Entities.Stream> query = db
            .Streams.AsNoTracking()
            .Where(s => s.ChannelId == broadcasterId);

        List<Domain.Stream.Entities.Stream> rows = streamId is null
            ? await query.ToListAsync(cancellationToken)
            : await query.Where(s => s.Id == streamId).ToListAsync(cancellationToken);

        // Ordered in memory: SQLite cannot order a DateTimeOffset column.
        return rows.Where(s => s.StartedAt is not null)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefault();
    }
}

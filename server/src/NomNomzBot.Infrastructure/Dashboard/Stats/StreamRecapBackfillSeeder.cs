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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Services;
using StreamEntity = NomNomzBot.Domain.Stream.Entities.Stream;

namespace NomNomzBot.Infrastructure.Dashboard.Stats;

/// <summary>
/// Gives every past stream a recap built from the recorded data, so the gallery reaches back past the streams that
/// happened to raid out. A stream gets a recap when it has none, or when the kept one no longer ends where the
/// stream ends (it was built while the stream was live, or the stream's end was repaired afterwards). Streams that
/// never ran (end at start) and duplicate rows of one start are skipped. Idempotent: a rebuilt recap ends where its
/// stream ends, so a re-run selects nothing. It orders after <c>StreamEndRepairSeeder</c>, so it counts the repaired
/// windows.
/// </summary>
public sealed class StreamRecapBackfillSeeder(
    IApplicationDbContext db,
    IStreamRecapService recaps,
    ILogger<StreamRecapBackfillSeeder> logger
) : ISeeder
{
    // After StreamEndRepairSeeder (940): a recap is a snapshot of the window, so the window must be final first.
    public int Order => 950;

    // Raise this date whenever a recap metric changes how it counts, so every kept recap is rebuilt once.
    private static readonly DateTime MetricsRevisedAt = new(
        2026,
        10,
        21,
        6,
        45,
        0,
        DateTimeKind.Utc
    );

    public async Task SeedAsync(CancellationToken ct = default)
    {
        List<StreamEntity> ended = await db
            .Streams.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.StartedAt != null && s.EndedAt != null)
            .ToListAsync(ct);
        // A recap built before the metrics were last revised keeps old numbers (for example a peak read from the
        // stored value only). Counting it as not matching rebuilds it once; the rebuild stamps UpdatedAt anew.
        Dictionary<string, DateTime> kept = await db
            .StreamRecaps.IgnoreQueryFilters()
            .AsNoTracking()
            .ToDictionaryAsync(
                r => r.ChannelId + "/" + r.StreamId,
                r => r.UpdatedAt < MetricsRevisedAt ? DateTime.MinValue : r.ToAt,
                ct
            );

        List<StreamEntity> pending = ended
            .Where(s => s.EndedAt > s.StartedAt)
            .GroupBy(s => (s.ChannelId, s.StartedAt))
            .Select(g => g.OrderByDescending(s => s.PeakViewers.HasValue).ThenBy(s => s.Id).First())
            .Where(s =>
                !kept.TryGetValue(s.ChannelId + "/" + s.Id, out DateTime keptTo)
                || keptTo != s.EndedAt!.Value.UtcDateTime
            )
            .OrderBy(s => s.ChannelId)
            .ThenBy(s => s.StartedAt)
            .ToList();

        int built = 0;
        foreach (StreamEntity stream in pending)
        {
            Result<StreamRecapDto> result = await recaps.BuildAsync(
                stream.ChannelId,
                stream.Id,
                null,
                ct
            );
            if (result.IsSuccess)
                built++;
            else
                logger.LogWarning(
                    "Recap backfill skipped stream {StreamId}: {Error}",
                    stream.Id,
                    result.ErrorMessage
                );
        }

        if (built > 0)
            logger.LogInformation("Recap backfill built {Count} stream recap(s)", built);
    }
}

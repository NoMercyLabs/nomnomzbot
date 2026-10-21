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

namespace NomNomzBot.Infrastructure.Stream;

/// <summary>
/// Repairs Stream rows closed by the old online handler, which stamped a missed-offline row with the NEXT
/// stream's <c>StartedAt</c>. Such a row claims the whole gap between two streams as live time. The repair
/// finds every row whose <c>EndedAt</c> equals the next row's <c>StartedAt</c> for the same channel and moves
/// the end to the last proof the stream was live (<see cref="StreamRowReconciler.LastActivityAsync"/>: the
/// latest chat or journaled viewer-count sample inside its window), else to the row's own start. A real
/// observed end is never equal to the next start, so a correct row is never selected. Idempotent: a repaired
/// row no longer matches, so a re-run selects nothing. The analytics duration is derived from
/// <c>EndedAt - StartedAt</c> on read, so repairing <c>EndedAt</c> repairs the duration. Plain EF, so it runs
/// the same on SQLite and Postgres.
/// </summary>
public sealed class StreamEndRepairSeeder : ISeeder
{
    private readonly IApplicationDbContext _db;
    private readonly ILogger<StreamEndRepairSeeder> _logger;

    public StreamEndRepairSeeder(IApplicationDbContext db, ILogger<StreamEndRepairSeeder> logger)
    {
        _db = db;
        _logger = logger;
    }

    // No stream of these channels runs longer than this unless activity proves it to the end.
    private static readonly TimeSpan MaxPlausibleLength = TimeSpan.FromHours(12);

    private static readonly TimeSpan StaleEndSlack = TimeSpan.FromHours(1);

    private static bool IsImplausiblyLong(Domain.Stream.Entities.Stream row) =>
        row.EndedAt - row.StartedAt > MaxPlausibleLength;

    // Late, like the other backfills: it only reads chat, the journal and the Streams rows themselves.
    public int Order => 940;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        List<Domain.Stream.Entities.Stream> rows = await _db
            .Streams.IgnoreQueryFilters()
            .Where(s => s.StartedAt != null)
            .OrderBy(s => s.ChannelId)
            .ThenBy(s => s.StartedAt)
            .ToListAsync(ct);

        int repaired = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            Domain.Stream.Entities.Stream row = rows[i];
            if (row.EndedAt is null)
                continue;

            DateTimeOffset? nextStart = rows.Skip(i + 1)
                .TakeWhile(r => r.ChannelId == row.ChannelId)
                .Select(r => r.StartedAt)
                .FirstOrDefault(s => s > row.StartedAt);

            // Window of the proof: up to the end the row claims, but never past the next stream's start.
            DateTimeOffset windowEnd =
                nextStart is { } next && next < row.EndedAt ? next : row.EndedAt.Value;
            bool stampedWithNextStart = row.EndedAt == nextStart;
            if (!stampedWithNextStart && !IsImplausiblyLong(row))
                continue;

            DateTimeOffset? lastActivity = await StreamRowReconciler.LastActivityAsync(
                _db,
                row.ChannelId,
                row,
                windowEnd,
                ct
            );
            DateTimeOffset proven = lastActivity ?? row.StartedAt!.Value;

            // A long stream that really ran to its stored end has activity up to it: leave that row alone.
            if (!stampedWithNextStart && row.EndedAt - proven <= StaleEndSlack)
                continue;

            row.EndedAt = proven;
            repaired++;
        }

        if (repaired == 0)
            return;

        await _db.SaveChangesAsync(ct);
        _logger.LogWarning(
            "Repaired {Count} stream row(s) whose end was stamped with the next stream's start",
            repaired
        );
    }
}

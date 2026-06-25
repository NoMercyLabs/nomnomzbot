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
using NomNomzBot.Application.Contracts.Analytics;

namespace NomNomzBot.Infrastructure.Analytics;

/// <summary>Finds the stream covering an instant from the durable <c>Streams</c> history (analytics.md §1.1).</summary>
public sealed class LiveWindowResolver(IApplicationDbContext db) : ILiveWindowResolver
{
    public async Task<string?> GetCoveringStreamIdAsync(
        Guid broadcasterId,
        DateTime at,
        CancellationToken ct = default
    )
    {
        DateTimeOffset instant = new(DateTime.SpecifyKind(at, DateTimeKind.Utc), TimeSpan.Zero);

        // The DateTimeOffset window comparison does not translate on the SQLite (self-host lite) provider — it
        // faulted the WatchSessionProjection's driver tick every pass. Filter the channel's streams in SQL
        // (ChannelId + StartedAt present — both translatable) and evaluate the time window in memory; Streams hold
        // one row per session, so a channel's set is tiny. The anonymous projection avoids materialising the whole
        // entity (and the Stream/System.IO.Stream name clash).
        var sessions = await db
            .Streams.Where(s => s.ChannelId == broadcasterId && s.StartedAt != null)
            .Select(s => new
            {
                s.Id,
                s.StartedAt,
                s.EndedAt,
            })
            .ToListAsync(ct);

        return sessions
            .Where(s => s.StartedAt <= instant && (s.EndedAt == null || s.EndedAt >= instant))
            .OrderByDescending(s => s.StartedAt)
            .Select(s => s.Id)
            .FirstOrDefault();
    }
}

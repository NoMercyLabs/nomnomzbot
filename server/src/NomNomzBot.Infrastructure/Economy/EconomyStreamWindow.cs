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

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;

namespace NomNomzBot.Infrastructure.Economy;

/// <summary>
/// Shared per-stream window for the economy caps (catalog / game / jar): the start of a channel's current
/// stream — its latest stream row, ordered by <c>CreatedAt</c> (SQLite cannot order by the
/// <c>DateTimeOffset StartedAt</c>; the latest-created stream is the current one). Null when the channel has no
/// stream record — then a per-stream cap is moot.
/// </summary>
internal static class EconomyStreamWindow
{
    public static async Task<DateTime?> CurrentStreamStartAsync(
        IApplicationDbContext db,
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        DateTimeOffset? startedAt = await db
            .Streams.Where(s => s.ChannelId == broadcasterId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => s.StartedAt)
            .FirstOrDefaultAsync(ct);
        return startedAt?.UtcDateTime;
    }
}

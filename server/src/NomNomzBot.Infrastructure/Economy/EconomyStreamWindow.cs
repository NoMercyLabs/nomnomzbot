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

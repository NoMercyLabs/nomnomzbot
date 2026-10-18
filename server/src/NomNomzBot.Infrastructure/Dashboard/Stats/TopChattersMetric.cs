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
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Stats;

namespace NomNomzBot.Infrastructure.Dashboard.Stats;

public sealed class TopChattersMetric(IApplicationDbContext db, StreamBanFilter bans)
    : StreamMetric<List<StreamStatsNamedCountDto>>
{
    public override string Key => "topChatters";

    protected override async Task<List<StreamStatsNamedCountDto>> ComputeAsync(
        StreamWindow window,
        CancellationToken ct
    )
    {
        BannedPeople banned = await bans.ForChannelAsync(window.ChannelId, ct);
        var rows = await db
            .ChatMessages.AsNoTracking()
            .InWindow(window)
            .WithoutBanned(banned)
            .Select(m => new { m.UserId, m.DisplayName })
            .ToListAsync(ct);
        return StreamRanking.Top(
            rows.GroupBy(r => r.UserId).Select(g => (g.First().DisplayName, g.Count()))
        );
    }

    protected override void Assign(StreamStatsDto stats, List<StreamStatsNamedCountDto> result) =>
        stats.TopChatters = result;
}

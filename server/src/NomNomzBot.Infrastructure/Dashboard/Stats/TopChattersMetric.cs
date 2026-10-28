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

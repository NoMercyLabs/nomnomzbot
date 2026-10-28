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

public sealed class CommandsRunMetric(IApplicationDbContext db, StreamBanFilter bans)
    : StreamMetric<StreamStatsRankedDto>
{
    public override string Key => "commandsRun";

    protected override async Task<StreamStatsRankedDto> ComputeAsync(
        StreamWindow window,
        CancellationToken ct
    )
    {
        DateTime from = window.From;
        DateTime to = window.To;
        BannedPeople banned = await bans.ForChannelAsync(window.ChannelId, ct);
        List<string> names = await db
            .CommandUsages.AsNoTracking()
            .WithoutBanned(banned)
            .Where(u =>
                u.BroadcasterId == window.ChannelId && u.CreatedAt >= from && u.CreatedAt <= to
            )
            .Select(u => u.CommandNameSnapshot)
            .ToListAsync(ct);
        return new() { Total = names.Count, Top = StreamRanking.TopOfNames(names) };
    }

    protected override void Assign(StreamStatsDto stats, StreamStatsRankedDto result) =>
        stats.CommandsRun = result;
}

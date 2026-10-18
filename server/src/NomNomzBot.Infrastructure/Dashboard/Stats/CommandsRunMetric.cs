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

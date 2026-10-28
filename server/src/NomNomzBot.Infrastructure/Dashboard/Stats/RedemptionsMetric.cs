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

public sealed class RedemptionsMetric(IApplicationDbContext db, StreamBanFilter bans)
    : StreamMetric<StreamStatsRankedDto>
{
    public override string Key => "redemptions";

    protected override async Task<StreamStatsRankedDto> ComputeAsync(
        StreamWindow window,
        CancellationToken ct
    )
    {
        DateTime from = window.From;
        DateTime to = window.To;
        BannedPeople banned = await bans.ForChannelAsync(window.ChannelId, ct);
        List<string> titles = await db
            .Redemptions.AsNoTracking()
            .WithoutBanned(banned)
            .Where(r =>
                r.BroadcasterId == window.ChannelId && r.RedeemedAt >= from && r.RedeemedAt <= to
            )
            .Select(r => r.RewardTitle)
            .ToListAsync(ct);
        return new() { Total = titles.Count, Top = StreamRanking.TopOfNames(titles) };
    }

    protected override void Assign(StreamStatsDto stats, StreamStatsRankedDto result) =>
        stats.Redemptions = result;
}

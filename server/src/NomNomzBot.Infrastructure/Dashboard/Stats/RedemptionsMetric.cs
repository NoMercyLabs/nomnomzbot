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

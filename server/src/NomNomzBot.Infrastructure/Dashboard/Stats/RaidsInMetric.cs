// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Stats;

namespace NomNomzBot.Infrastructure.Dashboard.Stats;

public sealed class RaidsInMetric(StreamFactReader facts) : StreamMetric<List<StreamStatsRaidDto>>
{
    public override string Key => "raidsIn";

    protected override async Task<List<StreamStatsRaidDto>> ComputeAsync(
        StreamWindow window,
        CancellationToken ct
    )
    {
        List<StreamFact> raids = await facts.OfTypeAsync(window, "channel.raid", ct);
        return raids
            .Select(f => new StreamStatsRaidDto
            {
                Name = f.Text("fromDisplayName") ?? StreamFact.Anonymous,
                Viewers = f.Int("viewerCount"),
            })
            .OrderByDescending(r => r.Viewers)
            .Take(StreamRanking.ListCap)
            .ToList();
    }

    protected override void Assign(StreamStatsDto stats, List<StreamStatsRaidDto> result) =>
        stats.RaidsIn = result;
}

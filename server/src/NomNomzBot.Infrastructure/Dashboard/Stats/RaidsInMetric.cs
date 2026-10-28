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

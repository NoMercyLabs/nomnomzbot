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

public sealed class GiftersMetric(StreamFactReader facts)
    : StreamMetric<List<StreamStatsNamedCountDto>>
{
    public override string Key => "gifters";

    protected override async Task<List<StreamStatsNamedCountDto>> ComputeAsync(
        StreamWindow window,
        CancellationToken ct
    )
    {
        List<StreamFact> gifts = await facts.OfTypeAsync(window, "channel.subscription.gift", ct);
        return StreamRanking.Top(
            gifts
                .GroupBy(f => f.NameOf("gifterDisplayName"))
                .Select(g => (g.Key, g.Sum(f => f.Int("giftCount"))))
        );
    }

    protected override void Assign(StreamStatsDto stats, List<StreamStatsNamedCountDto> result) =>
        stats.Gifters = result;
}

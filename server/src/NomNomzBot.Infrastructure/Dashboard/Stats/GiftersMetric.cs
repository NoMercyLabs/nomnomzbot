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

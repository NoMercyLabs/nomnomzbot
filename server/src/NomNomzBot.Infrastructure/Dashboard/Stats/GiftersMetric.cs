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
        List<StreamFact> batches = await facts.OfTypeAsync(window, "channel.subscription.gift", ct);
        List<StreamFact> notices = await facts.OfTypeAsync(
            window,
            "channel.subscription.gift.received",
            ct
        );

        // A gift batch event and its per-recipient notices describe the same gifts, so a gifter counts the larger
        // of the two. A single gift may arrive as a notice only, and a notice may be missing from a batch.
        Dictionary<string, int> fromBatches = batches
            .GroupBy(f => f.NameOf("gifterDisplayName"))
            .ToDictionary(g => g.Key, g => g.Sum(f => f.Int("giftCount")));
        Dictionary<string, int> fromNotices = notices
            .GroupBy(f => f.NameOf("gifterDisplayName"))
            .ToDictionary(g => g.Key, g => g.Count());

        return StreamRanking.All(
            fromBatches
                .Keys.Union(fromNotices.Keys)
                .Select(name =>
                    (
                        name,
                        Math.Max(
                            fromBatches.GetValueOrDefault(name),
                            fromNotices.GetValueOrDefault(name)
                        )
                    )
                )
        );
    }

    protected override void Assign(StreamStatsDto stats, List<StreamStatsNamedCountDto> result) =>
        stats.Gifters = result;
}

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

public sealed class BitsMetric(StreamFactReader facts) : StreamMetric<StreamStatsRankedDto>
{
    public override string Key => "bits";

    protected override async Task<StreamStatsRankedDto> ComputeAsync(
        StreamWindow window,
        CancellationToken ct
    )
    {
        List<StreamFact> cheers = await facts.OfTypeAsync(window, "channel.cheer", ct);
        return new()
        {
            Total = cheers.Sum(f => f.Int("bits")),
            Top = StreamRanking.Top(
                cheers
                    .GroupBy(f => f.NameOf("userDisplayName"))
                    .Select(g => (g.Key, g.Sum(f => f.Int("bits"))))
            ),
        };
    }

    protected override void Assign(StreamStatsDto stats, StreamStatsRankedDto result) =>
        stats.Bits = result;
}

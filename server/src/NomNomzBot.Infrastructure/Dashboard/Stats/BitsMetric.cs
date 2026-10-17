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

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

/// <summary>Who followed during the stream, oldest first; the count lives in <see cref="FollowersMetric"/>.</summary>
public sealed class NewFollowersMetric(StreamFactReader facts) : StreamMetric<List<string>>
{
    public override string Key => "newFollowers";

    protected override async Task<List<string>> ComputeAsync(
        StreamWindow window,
        CancellationToken ct
    )
    {
        List<StreamFact> follows = await facts.OfTypeAsync(window, "channel.follow", ct);
        return follows
            .OrderBy(f => f.OccurredAt())
            .Select(f => f.Text("userDisplayName"))
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal)
            .Take(StreamRanking.ListCap)
            .ToList();
    }

    protected override void Assign(StreamStatsDto stats, List<string> result) =>
        stats.NewFollowers = result;
}

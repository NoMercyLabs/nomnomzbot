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

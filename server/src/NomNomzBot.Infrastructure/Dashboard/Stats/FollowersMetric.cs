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

public sealed class FollowersMetric(StreamFactReader facts) : StreamMetric<int>
{
    public override string Key => "followers";

    protected override async Task<int> ComputeAsync(StreamWindow window, CancellationToken ct) =>
        (await facts.OfTypeAsync(window, "channel.follow", ct)).Count;

    protected override void Assign(StreamStatsDto stats, int result) => stats.Followers = result;
}

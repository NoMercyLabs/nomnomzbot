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

public sealed class FollowersMetric(StreamFactReader facts) : StreamMetric<int>
{
    public override string Key => "followers";

    protected override async Task<int> ComputeAsync(StreamWindow window, CancellationToken ct) =>
        (await facts.OfTypeAsync(window, "channel.follow", ct)).Count;

    protected override void Assign(StreamStatsDto stats, int result) => stats.Followers = result;
}

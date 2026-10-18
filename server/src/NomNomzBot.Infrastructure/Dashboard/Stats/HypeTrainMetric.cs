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

/// <summary>The hype trains that ended during the stream: the highest level reached and how many there were.</summary>
public sealed class HypeTrainMetric(StreamFactReader facts) : StreamMetric<StreamStatsHypeTrainDto?>
{
    public override string Key => "hypeTrain";

    protected override async Task<StreamStatsHypeTrainDto?> ComputeAsync(
        StreamWindow window,
        CancellationToken ct
    )
    {
        List<StreamFact> trains = await facts.OfTypeAsync(window, "channel.hype_train.end", ct);
        if (trains.Count == 0)
            return null;

        return new StreamStatsHypeTrainDto
        {
            Level = trains.Max(t => t.Int("level")),
            Trains = trains.Count,
        };
    }

    protected override void Assign(StreamStatsDto stats, StreamStatsHypeTrainDto? result) =>
        stats.HypeTrain = result;
}

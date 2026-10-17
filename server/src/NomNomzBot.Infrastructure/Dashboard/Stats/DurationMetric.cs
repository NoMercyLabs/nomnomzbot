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

/// <summary>Seconds the stream ran (to now while live).</summary>
public sealed class DurationMetric : StreamMetric<long>
{
    public override string Key => "duration";

    protected override Task<long> ComputeAsync(StreamWindow window, CancellationToken ct) =>
        Task.FromResult((long)Math.Max(0, (window.End - window.StartedAt).TotalSeconds));

    protected override void Assign(StreamStatsDto stats, long result) => stats.Duration = result;
}

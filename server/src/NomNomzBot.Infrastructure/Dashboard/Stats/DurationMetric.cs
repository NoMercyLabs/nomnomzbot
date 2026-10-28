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

/// <summary>Seconds the stream ran (to now while live).</summary>
public sealed class DurationMetric : StreamMetric<long>
{
    public override string Key => "duration";

    protected override Task<long> ComputeAsync(StreamWindow window, CancellationToken ct) =>
        Task.FromResult((long)Math.Max(0, (window.End - window.StartedAt).TotalSeconds));

    protected override void Assign(StreamStatsDto stats, long result) => stats.Duration = result;
}

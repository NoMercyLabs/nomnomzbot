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

namespace NomNomzBot.Application.Dashboard.Stats;

/// <summary>
/// One countable fact about a stream (followers, bits, top chatters, ...), asked for by <see cref="Key"/>.
/// A new metric is a new class plus a property on <see cref="StreamStatsDto"/>; it never adds an endpoint.
/// </summary>
public interface IStreamMetric
{
    /// <summary>The key a caller passes in <c>metrics</c>, and the name of the property it fills.</summary>
    string Key { get; }

    /// <summary>Counts the metric inside <paramref name="window"/> and writes it onto <paramref name="stats"/>.</summary>
    Task ComputeIntoAsync(
        StreamWindow window,
        StreamStatsDto stats,
        CancellationToken cancellationToken = default
    );
}

/// <summary>A metric with a typed result: <see cref="ComputeAsync"/> counts it, <see cref="Assign"/> places it.</summary>
public abstract class StreamMetric<TResult> : IStreamMetric
{
    public abstract string Key { get; }

    protected abstract Task<TResult> ComputeAsync(
        StreamWindow window,
        CancellationToken cancellationToken
    );

    protected abstract void Assign(StreamStatsDto stats, TResult result);

    public async Task ComputeIntoAsync(
        StreamWindow window,
        StreamStatsDto stats,
        CancellationToken cancellationToken = default
    )
    {
        TResult result = await ComputeAsync(window, cancellationToken);
        Assign(stats, result);
    }
}

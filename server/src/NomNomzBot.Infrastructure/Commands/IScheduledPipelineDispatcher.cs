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

namespace NomNomzBot.Infrastructure.Commands;

/// <summary>
/// Runs fired scheduled tasks in the background, so the sweep that fires them never waits on a pipeline run.
/// Runs of one channel go one after another in the order they were handed in; different channels run in parallel.
/// </summary>
public interface IScheduledPipelineDispatcher
{
    /// <summary>Queues the run behind its channel's earlier runs and returns at once.</summary>
    void Enqueue(ScheduledPipelineRun run);

    /// <summary>
    /// Waits until every queued and running run has finished. When <paramref name="cancellationToken"/> fires
    /// first, the runs still going are cancelled and the wait ends.
    /// </summary>
    Task DrainAsync(CancellationToken cancellationToken);
}

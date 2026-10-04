// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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

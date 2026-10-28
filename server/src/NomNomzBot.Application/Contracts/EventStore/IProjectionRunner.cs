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

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.EventStore;

/// <summary>
/// Drives projections forward from their checkpoints, and rebuilds them from zero. <see cref="RunOnceAsync"/>
/// advances one projection to the stream head; <see cref="RebuildAsync"/> resets the read model and replays
/// the whole stream (or the snapshot-folded tail) for a scope — the replay path the spec mandates.
/// </summary>
public interface IProjectionRunner
{
    /// <summary>
    /// Advances ONE projection from its checkpoint to the stream head, applying events in order and persisting
    /// the new <c>LastPosition</c> after each committed batch. A fault sets <c>Status=faulted</c> +
    /// <c>LastError</c> and does NOT advance past the bad event. Returns the number of events applied.
    /// </summary>
    Task<Result<long>> RunOnceAsync(
        string projectionName,
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Rebuilds ONE projection from zero: <c>ResetAsync</c> the read model for the scope, set the checkpoint to
    /// 0, then replay the whole stream through the upcaster so <c>ApplyAsync</c> sees only the current shape.
    /// Reset → replay yields the same state as the incremental fold. Returns the number of events applied.
    /// The optional <paramref name="progress"/> reports the cumulative applied-event count after each committed
    /// batch, so a long replay (e.g. an owner's ~41k-event backfill) is observable and a stall is pinpointable.
    /// </summary>
    Task<Result<long>> RebuildAsync(
        string projectionName,
        Guid? broadcasterId,
        CancellationToken cancellationToken = default,
        IProgress<long>? progress = null
    );

    /// <summary>Reads a projection's checkpoint (position, status, lag, last error). Read-only.</summary>
    Task<Result<ProjectionCheckpointDto>> GetCheckpointAsync(
        string projectionName,
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Lists all projection checkpoints (optionally one tenant) for the ops dashboard. Read-only.</summary>
    Task<Result<IReadOnlyList<ProjectionCheckpointDto>>> ListCheckpointsAsync(
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Sets <c>Status=paused</c> so the background driver skips it (manual intervention).</summary>
    Task<Result> PauseAsync(
        string projectionName,
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Clears paused/faulted back to <c>running</c> so the driver resumes from <c>LastPosition</c>.</summary>
    Task<Result> ResumeAsync(
        string projectionName,
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    );
}

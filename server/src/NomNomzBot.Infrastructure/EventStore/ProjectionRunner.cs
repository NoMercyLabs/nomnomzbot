// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.EventStore.Entities;

namespace NomNomzBot.Infrastructure.EventStore;

/// <summary>
/// Drives the registered <see cref="IProjection"/>s forward from their checkpoints and rebuilds them from zero.
/// Each event read from the journal is upcast to the current shape (<see cref="IEventUpcasterRegistry"/>)
/// before <c>ApplyAsync</c>, so projections never see a stale payload. Rebuild resets the read model, zeroes
/// the checkpoint, then replays the whole stream — reset → replay reconstructs the same state as the
/// incremental fold because <c>ApplyAsync</c> is idempotent. A fault parks the checkpoint at
/// <c>Status=faulted</c> without advancing past the bad event.
/// </summary>
public sealed class ProjectionRunner : IProjectionRunner
{
    private const int BatchSize = 500;

    private readonly IEnumerable<IProjection> _projections;
    private readonly IEventJournal _journal;
    private readonly IEventUpcasterRegistry _upcasters;
    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _clock;

    public ProjectionRunner(
        IEnumerable<IProjection> projections,
        IEventJournal journal,
        IEventUpcasterRegistry upcasters,
        IApplicationDbContext db,
        TimeProvider clock
    )
    {
        _projections = projections;
        _journal = journal;
        _upcasters = upcasters;
        _db = db;
        _clock = clock;
    }

    public async Task<Result<long>> RunOnceAsync(
        string projectionName,
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        Result<IProjection> projection = Resolve(projectionName);
        if (projection.IsFailure)
            return Result.Failure<long>(projection.ErrorMessage!, projection.ErrorCode);

        ProjectionCheckpoint checkpoint = await GetOrCreateCheckpointAsync(
            projectionName,
            broadcasterId,
            cancellationToken
        );

        return await DrainAsync(projection.Value, checkpoint, broadcasterId, cancellationToken);
    }

    public async Task<Result<long>> RebuildAsync(
        string projectionName,
        Guid? broadcasterId,
        CancellationToken cancellationToken = default,
        IProgress<long>? progress = null
    )
    {
        Result<IProjection> projection = Resolve(projectionName);
        if (projection.IsFailure)
            return Result.Failure<long>(projection.ErrorMessage!, projection.ErrorCode);

        Result reset = await projection.Value.ResetAsync(broadcasterId, cancellationToken);
        if (reset.IsFailure)
            return Result.Failure<long>(reset.ErrorMessage!, reset.ErrorCode, reset.ErrorDetail);

        ProjectionCheckpoint checkpoint = await GetOrCreateCheckpointAsync(
            projectionName,
            broadcasterId,
            cancellationToken
        );
        checkpoint.LastPosition = 0;
        checkpoint.Status = "rebuilding";
        checkpoint.LastError = null;
        checkpoint.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(cancellationToken);

        return await DrainAsync(
            projection.Value,
            checkpoint,
            broadcasterId,
            cancellationToken,
            progress
        );
    }

    // Reads forward batches from the checkpoint to the head, upcasts each event, applies it, and advances the
    // checkpoint after each committed batch. Global projections read the cross-tenant stream by Id; tenant
    // projections read the per-tenant stream by StreamPosition.
    private async Task<Result<long>> DrainAsync(
        IProjection projection,
        ProjectionCheckpoint checkpoint,
        Guid? broadcasterId,
        CancellationToken cancellationToken,
        IProgress<long>? progress = null
    )
    {
        long applied = 0;
        while (true)
        {
            Result<IReadOnlyList<EventRecord>> batch = projection.IsGlobal
                ? await _journal.ReadAllAsync(checkpoint.LastPosition, BatchSize, cancellationToken)
                : await _journal.ReadStreamAsync(
                    broadcasterId,
                    checkpoint.LastPosition,
                    BatchSize,
                    cancellationToken
                );

            if (batch.IsFailure)
                return Result.Failure<long>(batch.ErrorMessage!, batch.ErrorCode);

            if (batch.Value.Count == 0)
                break;

            foreach (EventRecord record in batch.Value)
            {
                long cursor = projection.IsGlobal ? record.Id : record.StreamPosition;

                if (!Subscribes(projection, record.EventType))
                {
                    checkpoint.LastPosition = cursor;
                    continue;
                }

                Result<EventRecord> upcast = Upcast(record);
                if (upcast.IsFailure)
                    return await FaultAsync(checkpoint, upcast.ErrorMessage!, cancellationToken);

                Result apply = await projection.ApplyAsync(upcast.Value, cancellationToken);
                if (apply.IsFailure)
                    return await FaultAsync(checkpoint, apply.ErrorMessage!, cancellationToken);

                checkpoint.LastPosition = cursor;
                applied++;
            }

            checkpoint.Status = "running";
            checkpoint.LastError = null;
            checkpoint.LastProcessedAt = _clock.GetUtcNow().UtcDateTime;
            checkpoint.UpdatedAt = checkpoint.LastProcessedAt.Value;
            await _db.SaveChangesAsync(cancellationToken);

            // Detach the entities this batch's ApplyAsync calls tracked (the projection's upserted read-model rows
            // plus the checkpoint). Without this, a full rebuild folds the whole stream through ONE scoped DbContext
            // and the change set grows with every applied event — each per-event SaveChanges then re-scans an
            // ever-larger graph (O(n²)), which makes a tens-of-thousands-event rebuild crawl toward a near-stall. The
            // batch is committed and projections re-read state with their own queries, so dropping the graph is safe.
            // The checkpoint is re-fetched fresh on the next batch via the tracked reference below.
            ClearChangeTracker();
            checkpoint = await ReattachCheckpointAsync(checkpoint, cancellationToken);

            progress?.Report(applied);

            if (batch.Value.Count < BatchSize)
                break;
        }

        return Result.Success(applied);
    }

    // After clearing the change tracker mid-rebuild, re-load the checkpoint so the next batch mutates a TRACKED
    // instance and its SaveChanges persists. Falls back to the now-detached instance (re-attached) only if the row
    // somehow can't be re-read, so the drain never loses its cursor.
    private async Task<ProjectionCheckpoint> ReattachCheckpointAsync(
        ProjectionCheckpoint checkpoint,
        CancellationToken cancellationToken
    )
    {
        ProjectionCheckpoint? fresh = await FindCheckpointAsync(
            checkpoint.ProjectionName,
            checkpoint.BroadcasterId,
            cancellationToken
        );
        if (fresh is not null)
            return fresh;

        _db.ProjectionCheckpoints.Attach(checkpoint);
        return checkpoint;
    }

    private void ClearChangeTracker()
    {
        if (_db is DbContext context)
            context.ChangeTracker.Clear();
    }

    private Result<EventRecord> Upcast(EventRecord record)
    {
        Result<UpcastResult> upcast = _upcasters.UpcastToCurrent(
            record.EventType,
            record.EventVersion,
            record.PayloadJson
        );
        if (upcast.IsFailure)
            return Result.Failure<EventRecord>(upcast.ErrorMessage!, upcast.ErrorCode);

        return upcast.Value.Changed
            ? Result.Success(
                record with
                {
                    PayloadJson = upcast.Value.PayloadJson,
                    EventVersion = upcast.Value.ToVersion,
                }
            )
            : Result.Success(record);
    }

    private async Task<Result<long>> FaultAsync(
        ProjectionCheckpoint checkpoint,
        string error,
        CancellationToken cancellationToken
    )
    {
        checkpoint.Status = "faulted";
        checkpoint.LastError = error;
        checkpoint.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Failure<long>(error, "PROJECTION_FAULTED");
    }

    public async Task<Result<ProjectionCheckpointDto>> GetCheckpointAsync(
        string projectionName,
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        ProjectionCheckpoint? checkpoint = await FindCheckpointAsync(
            projectionName,
            broadcasterId,
            cancellationToken
        );
        if (checkpoint is null)
            return Result.Failure<ProjectionCheckpointDto>(
                "Projection checkpoint not found.",
                "NOT_FOUND"
            );

        long head = (await _journal.GetHeadPositionAsync(broadcasterId, cancellationToken)).Value;
        return Result.Success(ToDto(checkpoint, head));
    }

    public async Task<Result<IReadOnlyList<ProjectionCheckpointDto>>> ListCheckpointsAsync(
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<ProjectionCheckpoint> checkpoints = await _db
            .ProjectionCheckpoints.Where(c =>
                broadcasterId == null || c.BroadcasterId == broadcasterId
            )
            .ToListAsync(cancellationToken);

        List<ProjectionCheckpointDto> dtos = [];
        foreach (ProjectionCheckpoint checkpoint in checkpoints)
        {
            long head = (
                await _journal.GetHeadPositionAsync(checkpoint.BroadcasterId, cancellationToken)
            ).Value;
            dtos.Add(ToDto(checkpoint, head));
        }

        return Result.Success<IReadOnlyList<ProjectionCheckpointDto>>(dtos);
    }

    public Task<Result> PauseAsync(
        string projectionName,
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    ) => SetStatusAsync(projectionName, broadcasterId, "paused", cancellationToken);

    public Task<Result> ResumeAsync(
        string projectionName,
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    ) => SetStatusAsync(projectionName, broadcasterId, "running", cancellationToken);

    private async Task<Result> SetStatusAsync(
        string projectionName,
        Guid? broadcasterId,
        string status,
        CancellationToken cancellationToken
    )
    {
        ProjectionCheckpoint? checkpoint = await FindCheckpointAsync(
            projectionName,
            broadcasterId,
            cancellationToken
        );
        if (checkpoint is null)
            return Result.Failure("Projection checkpoint not found.", "NOT_FOUND");

        checkpoint.Status = status;
        if (status == "running")
            checkpoint.LastError = null;
        checkpoint.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private Result<IProjection> Resolve(string projectionName)
    {
        IProjection? projection = _projections.FirstOrDefault(p => p.Name == projectionName);
        return projection is null
            ? Result.Failure<IProjection>(
                $"No projection registered with name '{projectionName}'.",
                "PROJECTION_NOT_FOUND"
            )
            : Result.Success(projection);
    }

    private static bool Subscribes(IProjection projection, string eventType) =>
        projection.SubscribedEventTypes.Count == 0
        || projection.SubscribedEventTypes.Contains(eventType);

    private async Task<ProjectionCheckpoint> GetOrCreateCheckpointAsync(
        string projectionName,
        Guid? broadcasterId,
        CancellationToken cancellationToken
    )
    {
        ProjectionCheckpoint? checkpoint = await FindCheckpointAsync(
            projectionName,
            broadcasterId,
            cancellationToken
        );
        if (checkpoint is not null)
            return checkpoint;

        checkpoint = new ProjectionCheckpoint
        {
            ProjectionName = projectionName,
            BroadcasterId = broadcasterId,
            LastPosition = 0,
            Status = "running",
            UpdatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        await _db.ProjectionCheckpoints.AddAsync(checkpoint, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return checkpoint;
    }

    private Task<ProjectionCheckpoint?> FindCheckpointAsync(
        string projectionName,
        Guid? broadcasterId,
        CancellationToken cancellationToken
    ) =>
        _db.ProjectionCheckpoints.FirstOrDefaultAsync(
            c => c.ProjectionName == projectionName && c.BroadcasterId == broadcasterId,
            cancellationToken
        );

    private static ProjectionCheckpointDto ToDto(ProjectionCheckpoint c, long head) =>
        new(
            c.ProjectionName,
            c.BroadcasterId,
            c.LastPosition,
            head,
            head - c.LastPosition,
            c.Status,
            c.LastError,
            c.LastProcessedAt,
            c.UpdatedAt
        );
}

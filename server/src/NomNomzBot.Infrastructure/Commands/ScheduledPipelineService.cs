// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;
using PipelineEntity = NomNomzBot.Domain.Commands.Entities.Pipeline;

namespace NomNomzBot.Infrastructure.Commands;

/// <summary>
/// <see cref="IScheduledPipelineService"/> over the <see cref="ScheduledPipelineTask"/> rows. Scheduling clamps
/// the delay to a safe range and (with a dedupe key) replaces the live pending row in place rather than stacking;
/// firing marks the row terminal BEFORE dispatch so a mid-dispatch crash can never re-fire, and a task overdue
/// beyond <see cref="StaleGrace"/> is expired instead of run (a long-late deferred action is wrong to fire).
/// </summary>
public sealed class ScheduledPipelineService : IScheduledPipelineService
{
    /// <summary>Delay floor — a "deferred" run is never immediate.</summary>
    internal const int MinDelaySeconds = 1;

    /// <summary>Delay ceiling — 24h; a longer horizon is out of scope for a one-shot deferral.</summary>
    internal const int MaxDelaySeconds = 24 * 60 * 60;

    /// <summary>
    /// How late a due task may still fire. Under steady state the 5s sweep keeps lateness to seconds; this only
    /// bites after downtime — a task overdue by more than this (e.g. after a multi-hour outage) is EXPIRED, since
    /// running a timed revert / auto-hide long after its moment is worse than not running it.
    /// </summary>
    internal static readonly TimeSpan StaleGrace = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IApplicationDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<ScheduledPipelineService> _logger;

    // The engine is resolved lazily on a fresh scope at fire time rather than constructor-injected: a `run_code`
    // action reaches IScriptRunner → IScheduledPipelineService (the schedule.pipeline capability), so a direct
    // engine dependency here would close a static DI cycle (engine → run_code → runner → scheduler → engine).
    // Dispatching on its own scope also isolates each deferred run's DbContext, matching the redemption path.
    public ScheduledPipelineService(
        IApplicationDbContext db,
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        ILogger<ScheduledPipelineService> logger
    )
    {
        _db = db;
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<ScheduledPipelineTaskDto>> ScheduleAsync(
        Guid broadcasterId,
        Guid pipelineId,
        int delaySeconds,
        IReadOnlyDictionary<string, string> variables,
        string triggeredByUserId,
        string triggeredByDisplayName,
        string? dedupeKey = null,
        CancellationToken cancellationToken = default
    )
    {
        string? pipelineName = await _db
            .Pipelines.Where(p => p.BroadcasterId == broadcasterId && p.Id == pipelineId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(cancellationToken);
        if (pipelineName is null)
            return Errors.NotFound<ScheduledPipelineTaskDto>("Pipeline", pipelineId.ToString());

        return await PersistAsync(
            broadcasterId,
            pipelineId,
            pipelineName,
            delaySeconds,
            variables,
            triggeredByUserId,
            triggeredByDisplayName,
            dedupeKey,
            cancellationToken
        );
    }

    public async Task<Result<ScheduledPipelineTaskDto>> ScheduleByNameAsync(
        Guid broadcasterId,
        string pipelineName,
        int delaySeconds,
        IReadOnlyDictionary<string, string> variables,
        string triggeredByUserId,
        string triggeredByDisplayName,
        string? dedupeKey = null,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(pipelineName))
            return Result.Failure<ScheduledPipelineTaskDto>(
                "A pipeline name is required.",
                "VALIDATION_FAILED"
            );

        string normalized = pipelineName.Trim();
        PipelineEntity? pipeline = await _db.Pipelines.FirstOrDefaultAsync(
            p =>
                p.BroadcasterId == broadcasterId
                && p.IsEnabled
                && p.Name.ToLower() == normalized.ToLower(),
            cancellationToken
        );
        if (pipeline is null)
            return Errors.NotFound<ScheduledPipelineTaskDto>("Pipeline", normalized);

        return await PersistAsync(
            broadcasterId,
            pipeline.Id,
            pipeline.Name,
            delaySeconds,
            variables,
            triggeredByUserId,
            triggeredByDisplayName,
            dedupeKey,
            cancellationToken
        );
    }

    private async Task<Result<ScheduledPipelineTaskDto>> PersistAsync(
        Guid broadcasterId,
        Guid pipelineId,
        string pipelineName,
        int delaySeconds,
        IReadOnlyDictionary<string, string> variables,
        string triggeredByUserId,
        string triggeredByDisplayName,
        string? dedupeKey,
        CancellationToken cancellationToken
    )
    {
        int clamped = Math.Clamp(delaySeconds, MinDelaySeconds, MaxDelaySeconds);
        DateTimeOffset now = _clock.GetUtcNow();
        DateTimeOffset dueAt = now.AddSeconds(clamped);
        string variablesJson = JsonSerializer.Serialize(
            variables.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            JsonOpts
        );

        // Dedupe: an existing PENDING task with the same key is REPLACED in place (its due time + payload updated),
        // so re-scheduling the same deferred action never stacks a second run.
        ScheduledPipelineTask? existing = dedupeKey is null
            ? null
            : await _db.ScheduledPipelineTasks.FirstOrDefaultAsync(
                t =>
                    t.BroadcasterId == broadcasterId
                    && t.DedupeKey == dedupeKey
                    && t.Status == ScheduledPipelineTaskStatus.Pending,
                cancellationToken
            );

        ScheduledPipelineTask task;
        if (existing is not null)
        {
            existing.PipelineId = pipelineId;
            existing.PipelineName = pipelineName;
            existing.DueAt = dueAt;
            existing.VariablesJson = variablesJson;
            existing.TriggeredByUserId = triggeredByUserId;
            existing.TriggeredByDisplayName = triggeredByDisplayName;
            task = existing;
        }
        else
        {
            task = new ScheduledPipelineTask
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = broadcasterId,
                PipelineId = pipelineId,
                PipelineName = pipelineName,
                DueAt = dueAt,
                VariablesJson = variablesJson,
                TriggeredByUserId = triggeredByUserId,
                TriggeredByDisplayName = triggeredByDisplayName,
                Status = ScheduledPipelineTaskStatus.Pending,
                DedupeKey = dedupeKey,
                CreatedAt = now,
            };
            _db.ScheduledPipelineTasks.Add(task);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(task));
    }

    public async Task<Result> CancelAsync(
        Guid broadcasterId,
        Guid taskId,
        CancellationToken cancellationToken = default
    )
    {
        ScheduledPipelineTask? task = await _db.ScheduledPipelineTasks.FirstOrDefaultAsync(
            t => t.BroadcasterId == broadcasterId && t.Id == taskId,
            cancellationToken
        );
        if (task is null)
            return Result.Failure($"Scheduled task '{taskId}' was not found.", "NOT_FOUND");

        if (task.Status != ScheduledPipelineTaskStatus.Pending)
            return Result.Success(); // already terminal — cancelling is idempotent

        task.Status = ScheduledPipelineTaskStatus.Cancelled;
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> CancelByDedupeKeyAsync(
        Guid broadcasterId,
        string dedupeKey,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(dedupeKey))
            return Result.Failure("A dedupe key is required.", "VALIDATION_FAILED");

        List<ScheduledPipelineTask> pending = await _db
            .ScheduledPipelineTasks.Where(t =>
                t.BroadcasterId == broadcasterId
                && t.DedupeKey == dedupeKey
                && t.Status == ScheduledPipelineTaskStatus.Pending
            )
            .ToListAsync(cancellationToken);

        foreach (ScheduledPipelineTask task in pending)
            task.Status = ScheduledPipelineTaskStatus.Cancelled;

        if (pending.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<IReadOnlyList<ScheduledPipelineTaskDto>> ListPendingAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<ScheduledPipelineTask> pending = await _db
            .ScheduledPipelineTasks.Where(t =>
                t.BroadcasterId == broadcasterId && t.Status == ScheduledPipelineTaskStatus.Pending
            )
            .OrderBy(t => t.DueAt)
            .ToListAsync(cancellationToken);
        return pending.Select(ToDto).ToList();
    }

    public async Task<int> FireDueAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _clock.GetUtcNow();

        // Cross-tenant read: the sweeper runs in a background scope (no ambient tenant), so the tenant query
        // filter is a no-op and every channel's due rows are visible — the same shape RedemptionTimer uses.
        List<ScheduledPipelineTask> due = await _db
            .ScheduledPipelineTasks.Where(t =>
                t.Status == ScheduledPipelineTaskStatus.Pending && t.DueAt <= now
            )
            .OrderBy(t => t.DueAt)
            .ToListAsync(cancellationToken);
        if (due.Count == 0)
            return 0;

        DateTimeOffset staleBefore = now - StaleGrace;
        List<ScheduledPipelineTask> toDispatch = [];

        // Persist the terminal transition FIRST: a task marked fired here can never fire twice, even if the
        // dispatch below crashes the process — the row already reads terminal on the next sweep.
        foreach (ScheduledPipelineTask task in due)
        {
            task.FiredAt = now;
            if (task.DueAt < staleBefore)
            {
                task.Status = ScheduledPipelineTaskStatus.Expired;
                _logger.LogWarning(
                    "Scheduled pipeline {TaskId} for channel {Channel} was {Overdue} overdue — expired without firing",
                    task.Id,
                    task.BroadcasterId,
                    now - task.DueAt
                );
            }
            else
            {
                task.Status = ScheduledPipelineTaskStatus.Fired;
                toDispatch.Add(task);
            }
        }
        await _db.SaveChangesAsync(cancellationToken);

        foreach (ScheduledPipelineTask task in toDispatch)
            await DispatchAsync(task, cancellationToken);

        return due.Count;
    }

    /// <summary>
    /// Best-effort dispatch through the pipeline engine. The engine returns a failed result (never throws) when
    /// the target pipeline was deleted or won't parse; a genuine fault is caught and logged so one bad task can
    /// never break the sweep or leave a poisoned row behind (the row is already marked fired).
    /// </summary>
    private async Task DispatchAsync(ScheduledPipelineTask task, CancellationToken ct)
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IPipelineEngine engine = scope.ServiceProvider.GetRequiredService<IPipelineEngine>();
            PipelineExecutionResult result = await engine.ExecuteAsync(
                new PipelineRequest
                {
                    BroadcasterId = task.BroadcasterId,
                    PipelineId = task.PipelineId,
                    TriggeredByUserId = task.TriggeredByUserId,
                    TriggeredByDisplayName = task.TriggeredByDisplayName,
                    InitialVariables = DeserializeVariables(task.VariablesJson),
                },
                ct
            );
            if (result.Outcome == PipelineOutcome.Failed)
                _logger.LogWarning(
                    "Deferred pipeline {PipelineId} ({PipelineName}) for channel {Channel} fired but failed: {Error}",
                    task.PipelineId,
                    task.PipelineName,
                    task.BroadcasterId,
                    result.ErrorMessage
                );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Deferred pipeline {PipelineId} for channel {Channel} threw during dispatch",
                task.PipelineId,
                task.BroadcasterId
            );
        }
    }

    private static Dictionary<string, string> DeserializeVariables(string variablesJson)
    {
        if (string.IsNullOrWhiteSpace(variablesJson))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            Dictionary<string, string>? parsed = JsonSerializer.Deserialize<
                Dictionary<string, string>
            >(variablesJson, JsonOpts);
            return parsed is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static ScheduledPipelineTaskDto ToDto(ScheduledPipelineTask t) =>
        new(
            t.Id,
            t.PipelineId,
            t.PipelineName,
            t.DueAt,
            t.Status,
            t.DedupeKey,
            t.TriggeredByDisplayName,
            t.CreatedAt
        );
}

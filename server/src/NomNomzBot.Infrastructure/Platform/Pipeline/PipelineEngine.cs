// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Platform.Pipeline;

/// <summary>
/// Executes user-defined command pipelines.
///
/// Step source priority:
///   1. When PipelineRequest.PipelineId is set: load PipelineStep rows from the database,
///      ordered by Order ascending. Falls back to GraphJsonCache if no rows found.
///   2. When PipelineId is null: parse PipelineRequest.PipelineJson directly.
///
/// Limits:
///   - Max 5 concurrent pipelines per channel
///   - Max 5-minute execution timeout per pipeline
///   - Cancelled when channel goes offline
/// </summary>
public sealed class PipelineEngine : IPipelineEngine
{
    private static readonly TimeSpan ExecutionTimeout = TimeSpan.FromMinutes(5);
    private const int MaxConcurrentPerChannel = 5;

    // Retention: a busy channel runs pipelines constantly, so PipelineExecution rows are bounded
    // on two axes rather than kept forever. Successful runs (Completed/Stopped) are routine noise —
    // they carry no debugging value once quiet, so they're purged fast. Failure-shaped outcomes
    // (PartiallyFailed/TimedOut/Cancelled/Failed) are what a streamer actually needs to diagnose
    // "why did my command misbehave", so they get a longer window. A hard per-channel row cap is
    // enforced on top of both TTLs so an extreme-volume channel can never grow this table unbounded
    // even inside the retention window.
    private static readonly TimeSpan SuccessRetention = TimeSpan.FromDays(3);
    private static readonly TimeSpan FailureRetention = TimeSpan.FromDays(30);
    private const int MaxRowsPerChannel = 500;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IApplicationDbContext _db;
    private readonly IChannelRegistry _registry;
    private readonly IEnumerable<ICommandAction> _actions;
    private readonly IEnumerable<ICommandCondition> _conditions;
    private readonly ILogger<PipelineEngine> _logger;
    private readonly TimeProvider _timeProvider;

    // Per-channel active count (separate from the CancellationTokenSources in ChannelContext).
    // Keyed by the tenant (channel) Guid.
    private readonly ConcurrentDictionary<Guid, int> _activeCount = new();

    public PipelineEngine(
        IApplicationDbContext db,
        IChannelRegistry registry,
        IEnumerable<ICommandAction> actions,
        IEnumerable<ICommandCondition> conditions,
        ILogger<PipelineEngine> logger,
        TimeProvider timeProvider
    )
    {
        _db = db;
        _registry = registry;
        _actions = actions;
        _conditions = conditions;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public int GetActiveCountForChannel(Guid broadcasterId) =>
        _activeCount.GetValueOrDefault(broadcasterId, 0);

    public async Task CancelAllForChannelAsync(Guid broadcasterId)
    {
        ChannelContext? ctx = _registry.Get(broadcasterId);
        if (ctx is null)
            return;

        foreach ((string id, CancellationTokenSource cts) in ctx.ActivePipelines)
        {
            try
            {
                await cts.CancelAsync();
            }
            catch
            { /* best-effort */
            }
        }

        _logger.LogInformation(
            "Cancelled all pipelines for channel {BroadcasterId}",
            broadcasterId
        );
    }

    public async Task<PipelineExecutionResult> ExecuteAsync(
        PipelineRequest request,
        CancellationToken ct = default
    )
    {
        DateTimeOffset startedAt = _timeProvider.GetUtcNow();

        // Concurrency gate
        int current = _activeCount.AddOrUpdate(request.BroadcasterId, 1, (_, v) => v + 1);
        if (current > MaxConcurrentPerChannel)
        {
            _activeCount.AddOrUpdate(request.BroadcasterId, 0, (_, v) => Math.Max(0, v - 1));
            PipelineExecutionResult throttled = new()
            {
                ExecutionId = Guid.NewGuid().ToString("N")[..12],
                Outcome = PipelineOutcome.Failed,
                Duration = TimeSpan.Zero,
                ErrorMessage =
                    $"Channel {request.BroadcasterId} has too many active pipelines ({MaxConcurrentPerChannel} max)",
            };
            await PersistExecutionAsync(request, null, startedAt, throttled, ct);
            return throttled;
        }

        // Resolve the pipeline definition — DB steps take priority over graph JSON cache.
        PipelineDefinition? definition;
        if (request.PipelineId.HasValue)
        {
            definition = await LoadFromDbAsync(request.PipelineId.Value, ct);
            if (definition is null || definition.Steps.Count == 0)
            {
                // Fall back to graph JSON cache if DB steps are absent.
                definition = ParseJson(request.PipelineJson);
            }
        }
        else
        {
            definition = ParseJson(request.PipelineJson);
        }

        if (definition is null)
        {
            _activeCount.AddOrUpdate(request.BroadcasterId, 0, (_, v) => Math.Max(0, v - 1));
            PipelineExecutionResult invalid = new()
            {
                ExecutionId = Guid.NewGuid().ToString("N")[..12],
                Outcome = PipelineOutcome.Failed,
                Duration = _timeProvider.GetUtcNow() - startedAt,
                ErrorMessage =
                    "Invalid pipeline: could not parse JSON or load steps from database.",
            };
            await PersistExecutionAsync(request, null, startedAt, invalid, ct);
            return invalid;
        }

        if (definition.Steps.Count == 0)
        {
            _activeCount.AddOrUpdate(request.BroadcasterId, 0, (_, v) => Math.Max(0, v - 1));
            PipelineExecutionResult empty = new()
            {
                ExecutionId = Guid.NewGuid().ToString("N")[..12],
                Outcome = PipelineOutcome.Completed,
                Duration = _timeProvider.GetUtcNow() - startedAt,
            };
            await PersistExecutionAsync(request, definition, startedAt, empty, ct);
            return empty;
        }

        // Build execution context
        PipelineExecutionContext execCtx = new()
        {
            BroadcasterId = request.BroadcasterId,
            TriggeredByUserId = request.TriggeredByUserId,
            TriggeredByDisplayName = request.TriggeredByDisplayName,
            MessageId = request.MessageId ?? string.Empty,
            RedemptionId = request.RedemptionId,
            RewardId = request.RewardId,
            RawMessage = request.RawMessage,
            CancellationToken = ct,
        };

        // Seed initial variables
        foreach ((string k, string v) in request.InitialVariables)
            execCtx.Variables[k] = v;

        // Register for cancellation via ChannelContext
        using CancellationTokenSource timeoutCts = new(ExecutionTimeout);
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            ct,
            timeoutCts.Token
        );
        ChannelContext? channelCtx = _registry.Get(request.BroadcasterId);
        if (channelCtx is not null)
            channelCtx.ActivePipelines[execCtx.ExecutionId] = linkedCts;

        PipelineExecutionResult result;
        try
        {
            result = await RunStepsAsync(execCtx, definition, startedAt, linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Pipeline {ExecutionId} timed out in channel {BroadcasterId}",
                execCtx.ExecutionId,
                request.BroadcasterId
            );
            result = new()
            {
                ExecutionId = execCtx.ExecutionId,
                Outcome = PipelineOutcome.TimedOut,
                Duration = _timeProvider.GetUtcNow() - startedAt,
                StepsExecuted = execCtx.CurrentStepIndex,
                Total = definition.Steps.Count,
                StepLogs = execCtx.StepLogs,
            };
        }
        catch (OperationCanceledException)
        {
            result = new()
            {
                ExecutionId = execCtx.ExecutionId,
                Outcome = PipelineOutcome.Cancelled,
                Duration = _timeProvider.GetUtcNow() - startedAt,
                StepsExecuted = execCtx.CurrentStepIndex,
                Total = definition.Steps.Count,
                StepLogs = execCtx.StepLogs,
            };
        }
        finally
        {
            channelCtx?.ActivePipelines.TryRemove(execCtx.ExecutionId, out _);
            _activeCount.AddOrUpdate(request.BroadcasterId, 0, (_, v) => Math.Max(0, v - 1));
        }

        // Persist outside the finally block: SaveChangesAsync can itself throw (e.g. transient DB
        // failure) and must not mask the pipeline's own outcome above, nor prevent the active-count
        // decrement in `finally` from running first.
        await PersistExecutionAsync(request, definition, startedAt, result, ct);
        return result;
    }

    // ─── Execution loop ───────────────────────────────────────────────────────

    private async Task<PipelineExecutionResult> RunStepsAsync(
        PipelineExecutionContext ctx,
        PipelineDefinition definition,
        DateTimeOffset startedAt,
        CancellationToken ct
    )
    {
        int executed = 0;
        int skipped = 0;

        // Distinguishes WHY the loop broke early: a failed action (fail-CLOSED, no continue_on_error)
        // must report PartiallyFailed, never Completed — a run that never reached its last step because
        // something broke must never masquerade as a clean finish. A deliberate `stop` action or a
        // matched `stop_on_match` step is Stopped instead: the command did its intended work.
        bool failedBreak = false;
        bool stoppedDeliberately = false;

        for (int i = 0; i < definition.Steps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            ctx.CurrentStepIndex = i;

            PipelineStepDefinition step = definition.Steps[i];
            DateTimeOffset stepStart = _timeProvider.GetUtcNow();

            // Evaluate condition (skip step if condition false)
            if (step.Condition is not null && !await EvaluateConditionAsync(ctx, step.Condition))
            {
                skipped++;
                ctx.StepLogs.Add(
                    new()
                    {
                        StepIndex = i,
                        ActionType = step.Action.Type,
                        Succeeded = true,
                        Duration = _timeProvider.GetUtcNow() - stepStart,
                        Output = "Condition not met — step skipped",
                    }
                );
                continue;
            }

            // Execute action
            ActionResult actionResult;
            try
            {
                actionResult = await ExecuteActionAsync(ctx, step.Action, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Pipeline action {ActionType} failed at step {StepIndex}",
                    step.Action.Type,
                    i
                );
                ctx.StepLogs.Add(
                    new()
                    {
                        StepIndex = i,
                        ActionType = step.Action.Type,
                        Succeeded = false,
                        Duration = _timeProvider.GetUtcNow() - stepStart,
                        ErrorMessage = ex.Message,
                    }
                );
                // Fail-CLOSED: an unhandled exception from an action aborts the pipeline.
                failedBreak = true;
                break;
            }

            ctx.StepLogs.Add(
                new()
                {
                    StepIndex = i,
                    ActionType = step.Action.Type,
                    Succeeded = actionResult.Succeeded,
                    Duration = _timeProvider.GetUtcNow() - stepStart,
                    Output = actionResult.Output,
                    ErrorMessage = actionResult.ErrorMessage,
                }
            );

            // Expose the just-run action's outcome as pipeline variables so a LATER step can branch on it
            // (e.g. `play_tts` with continue_on_error, then a `redemption_refund` gated by a comparison on
            // {last.success} == false). Without this a pipeline could proceed past a failure but never react
            // to it — the generic building block behind the legacy "refund on failed queue / empty TTS" flows.
            ctx.Variables["last.success"] = actionResult.Succeeded ? "true" : "false";
            ctx.Variables["last.output"] = actionResult.Output ?? string.Empty;
            ctx.Variables["last.error"] = actionResult.ErrorMessage ?? string.Empty;

            if (actionResult.Succeeded)
            {
                executed++;
            }
            else if (!step.ContinueOnError)
            {
                // Fail-CLOSED: a failed action stops the pipeline unless the step opts in to continue.
                failedBreak = true;
                break;
            }

            // Check stop flag — a deliberate stop, not a failure.
            if (ctx.ShouldStop || (step.StopOnMatch && actionResult.Succeeded))
            {
                stoppedDeliberately = true;
                break;
            }
        }

        PipelineOutcome outcome =
            failedBreak ? PipelineOutcome.PartiallyFailed
            : stoppedDeliberately ? PipelineOutcome.Stopped
            : PipelineOutcome.Completed;

        return new()
        {
            ExecutionId = ctx.ExecutionId,
            Outcome = outcome,
            Duration = _timeProvider.GetUtcNow() - startedAt,
            StepsExecuted = executed,
            StepsSkipped = skipped,
            Total = definition.Steps.Count,
            StepLogs = ctx.StepLogs,
        };
    }

    private async Task<bool> EvaluateConditionAsync(
        PipelineExecutionContext ctx,
        ConditionDefinition condition
    )
    {
        ICommandCondition? evaluator = _conditions.FirstOrDefault(c =>
            string.Equals(c.ConditionType, condition.Type, StringComparison.OrdinalIgnoreCase)
        );

        if (evaluator is null)
        {
            // Fail-CLOSED: an unrecognized condition type blocks execution rather than permitting it.
            _logger.LogError(
                "Unknown condition type '{Type}' — blocking step (fail-closed)",
                condition.Type
            );
            return false;
        }

        return await evaluator.EvaluateAsync(ctx, condition);
    }

    private async Task<ActionResult> ExecuteActionAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action,
        CancellationToken ct
    )
    {
        ICommandAction? executor = _actions.FirstOrDefault(a =>
            string.Equals(a.ActionType, action.Type, StringComparison.OrdinalIgnoreCase)
        );

        if (executor is null)
        {
            // Fail-CLOSED: unknown action type aborts the step (caller breaks on failure).
            _logger.LogError("Unknown action type '{Type}' — fail-closed", action.Type);
            return ActionResult.Failure($"Unknown action type '{action.Type}'");
        }

        return await executor.ExecuteAsync(ctx, action);
    }

    // ─── Step resolution helpers ─────────────────────────────────────────────

    private async Task<PipelineDefinition?> LoadFromDbAsync(Guid pipelineId, CancellationToken ct)
    {
        List<PipelineStep> rows = await _db
            .PipelineSteps.Where(s => s.PipelineId == pipelineId && s.IsEnabled)
            .OrderBy(s => s.Order)
            .ToListAsync(ct);

        if (rows.Count == 0)
            return null;

        PipelineDefinition definition = new();
        foreach (PipelineStep row in rows)
        {
            ActionDefinition? action;
            try
            {
                action = JsonSerializer.Deserialize<ActionDefinition>(row.ConfigJson, JsonOpts);
            }
            catch
            {
                action = null;
            }

            if (action is null)
            {
                _logger.LogWarning(
                    "PipelineStep {StepId} has invalid ConfigJson — skipping",
                    row.Id
                );
                continue;
            }

            // ActionType from the row overrides whatever may be embedded in ConfigJson.
            action.Type = row.ActionType;

            // Translate DB conditions to runtime ConditionDefinition list.
            ConditionDefinition? condition = null;
            if (row.Conditions is { Count: > 0 })
            {
                PipelineStepCondition first = row.Conditions.OrderBy(c => c.Order).First();
                condition = new()
                {
                    Type = first.ConditionType,
                    Parameters = new()
                    {
                        ["operator"] = JsonSerializer.SerializeToElement(
                            first.Operator ?? "eq",
                            JsonOpts
                        ),
                        ["left"] = JsonSerializer.SerializeToElement(
                            first.LeftOperand ?? string.Empty,
                            JsonOpts
                        ),
                        ["right"] = JsonSerializer.SerializeToElement(
                            first.RightOperand ?? string.Empty,
                            JsonOpts
                        ),
                        ["negate"] = JsonSerializer.SerializeToElement(first.Negate, JsonOpts),
                    },
                };
            }

            definition.Steps.Add(new() { Action = action, Condition = condition });
        }

        return definition;
    }

    // ─── Persistence (H.4 PipelineExecution) ─────────────────────────────────

    private async Task PersistExecutionAsync(
        PipelineRequest request,
        PipelineDefinition? definition,
        DateTimeOffset startedAt,
        PipelineExecutionResult result,
        CancellationToken ct
    )
    {
        Guid? triggeredByUserId = Guid.TryParse(request.TriggeredByUserId, out Guid parsedUserId)
            ? parsedUserId
            : null;

        // Step logs exclude StepExecutionLog.Output — it can carry chat/user content — per the
        // append-only, PII-excluded contract on PipelineExecution.StepLogsJson.
        string? stepLogsJson =
            result.StepLogs.Count == 0
                ? null
                : JsonSerializer.Serialize(
                    result.StepLogs.Select(l => new
                    {
                        l.StepIndex,
                        l.ActionType,
                        l.Succeeded,
                        DurationMs = (int)l.Duration.TotalMilliseconds,
                        l.ErrorMessage,
                    })
                );

        PipelineExecution row = new()
        {
            PipelineId = request.PipelineId ?? Guid.Empty,
            BroadcasterId = request.BroadcasterId,
            TriggeredByUserId = triggeredByUserId,
            TriggerKind = request.PipelineId.HasValue ? "pipeline" : "inline_json",
            Status = ToStatus(result.Outcome),
            HostCallCount = result.StepsExecuted,
            DurationMs = (int)result.Duration.TotalMilliseconds,
            ErrorMessage = result.ErrorMessage,
            StepLogsJson = stepLogsJson,
            StartedAt = startedAt.UtcDateTime,
            CompletedAt = startedAt.UtcDateTime.Add(result.Duration),
        };

        try
        {
            _db.PipelineExecutions.Add(row);
            await _db.SaveChangesAsync(ct);
            await PurgeOldExecutionsAsync(request.BroadcasterId, ct);
        }
        catch (Exception ex)
        {
            // Telemetry persistence must never take down command execution — the run already
            // completed (or failed) and its result has already been returned to the caller.
            _logger.LogError(
                ex,
                "Failed to persist PipelineExecution for pipeline {PipelineId} in channel {BroadcasterId}",
                request.PipelineId,
                request.BroadcasterId
            );
        }
    }

    private static string ToStatus(PipelineOutcome outcome) =>
        outcome switch
        {
            PipelineOutcome.Completed => "completed",
            PipelineOutcome.Stopped => "stopped",
            PipelineOutcome.Failed => "failed",
            PipelineOutcome.PartiallyFailed => "partially_failed",
            PipelineOutcome.TimedOut => "timed_out",
            PipelineOutcome.Cancelled => "cancelled",
            _ => "unknown",
        };

    private static readonly HashSet<string> FailureStatuses =
    [
        "failed",
        "partially_failed",
        "timed_out",
        "cancelled",
    ];

    /// <summary>
    /// Retention sweep for the channel that just ran a pipeline. Successful/stopped runs are
    /// routine noise and expire after <see cref="SuccessRetention"/>; failure-shaped outcomes are
    /// kept longer (<see cref="FailureRetention"/>) so a streamer can actually diagnose a
    /// misbehaving command. A hard <see cref="MaxRowsPerChannel"/> cap bounds disk usage even
    /// inside those windows for an extreme-volume channel.
    /// </summary>
    private async Task PurgeOldExecutionsAsync(Guid broadcasterId, CancellationToken ct)
    {
        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        DateTime successCutoff = now - SuccessRetention;
        DateTime failureCutoff = now - FailureRetention;

        await _db
            .PipelineExecutions.Where(e =>
                e.BroadcasterId == broadcasterId
                && (
                    (!FailureStatuses.Contains(e.Status) && e.StartedAt < successCutoff)
                    || (FailureStatuses.Contains(e.Status) && e.StartedAt < failureCutoff)
                )
            )
            .ExecuteDeleteAsync(ct);

        int total = await _db
            .PipelineExecutions.Where(e => e.BroadcasterId == broadcasterId)
            .CountAsync(ct);

        if (total > MaxRowsPerChannel)
        {
            List<long> overflowIds = await _db
                .PipelineExecutions.Where(e => e.BroadcasterId == broadcasterId)
                .OrderByDescending(e => e.StartedAt)
                .Skip(MaxRowsPerChannel)
                .Select(e => e.Id)
                .ToListAsync(ct);

            if (overflowIds.Count > 0)
            {
                await _db
                    .PipelineExecutions.Where(e => overflowIds.Contains(e.Id))
                    .ExecuteDeleteAsync(ct);
            }
        }
    }

    private PipelineDefinition? ParseJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return new();
        try
        {
            PipelineDefinition? parsed = JsonSerializer.Deserialize<PipelineDefinition>(
                json,
                JsonOpts
            );
            return parsed ?? new PipelineDefinition();
        }
        catch (Exception ex)
        {
            _logger.LogError("Pipeline JSON parse failed: {Error}", ex.Message);
            return null;
        }
    }
}

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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Platform.Pipeline;

/// <summary>
/// Executes user-defined command pipelines.
///
/// Pipeline JSON format:
/// {
///   "steps": [
///     {
///       "condition": { "type": "user_role", "min_role": "moderator" },
///       "stop_on_match": false,
///       "action": { "type": "send_message", "message": "Hello {user}!" }
///     }
///   ]
/// }
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

    private readonly IChannelRegistry _registry;
    private readonly IEnumerable<ICommandAction> _actions;
    private readonly IEnumerable<ICommandCondition> _conditions;
    private readonly ILogger<PipelineEngine> _logger;
    private readonly TimeProvider _timeProvider;

    // Per-channel active count (separate from the CancellationTokenSources in ChannelContext).
    // Keyed by the tenant (channel) Guid.
    private readonly ConcurrentDictionary<Guid, int> _activeCount = new();

    public PipelineEngine(
        IChannelRegistry registry,
        IEnumerable<ICommandAction> actions,
        IEnumerable<ICommandCondition> conditions,
        ILogger<PipelineEngine> logger,
        TimeProvider timeProvider
    )
    {
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
            return new()
            {
                ExecutionId = Guid.NewGuid().ToString("N")[..12],
                Outcome = PipelineOutcome.Failed,
                Duration = TimeSpan.Zero,
                ErrorMessage =
                    $"Channel {request.BroadcasterId} has too many active pipelines ({MaxConcurrentPerChannel} max)",
            };
        }

        // Parse the pipeline definition
        PipelineDefinition? definition;
        try
        {
            definition = JsonSerializer.Deserialize<PipelineDefinition>(
                request.PipelineJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );
        }
        catch (Exception ex)
        {
            _activeCount.AddOrUpdate(request.BroadcasterId, 0, (_, v) => Math.Max(0, v - 1));
            return new()
            {
                ExecutionId = Guid.NewGuid().ToString("N")[..12],
                Outcome = PipelineOutcome.Failed,
                Duration = _timeProvider.GetUtcNow() - startedAt,
                ErrorMessage = $"Invalid pipeline JSON: {ex.Message}",
            };
        }

        if (definition is null || definition.Steps.Count == 0)
        {
            _activeCount.AddOrUpdate(request.BroadcasterId, 0, (_, v) => Math.Max(0, v - 1));
            return new()
            {
                ExecutionId = Guid.NewGuid().ToString("N")[..12],
                Outcome = PipelineOutcome.Completed,
                Duration = _timeProvider.GetUtcNow() - startedAt,
            };
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

        try
        {
            return await RunStepsAsync(execCtx, definition, startedAt, linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Pipeline {ExecutionId} timed out in channel {BroadcasterId}",
                execCtx.ExecutionId,
                request.BroadcasterId
            );
            return new()
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
            return new()
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

        for (int i = 0; i < definition.Steps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            ctx.CurrentStepIndex = i;

            PipelineStepDefinition step = definition.Steps[i];
            DateTimeOffset stepStart = _timeProvider.GetUtcNow();

            // Evaluate condition (skip step if condition false)
            if (step.Condition is not null && !EvaluateCondition(ctx, step.Condition))
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

            if (actionResult.Succeeded)
            {
                executed++;
            }
            else if (!step.ContinueOnError)
            {
                // Fail-CLOSED: a failed action stops the pipeline unless the step opts in to continue.
                break;
            }

            // Check stop flag
            if (ctx.ShouldStop || (step.StopOnMatch && actionResult.Succeeded))
                break;
        }

        return new()
        {
            ExecutionId = ctx.ExecutionId,
            Outcome = PipelineOutcome.Completed,
            Duration = _timeProvider.GetUtcNow() - startedAt,
            StepsExecuted = executed,
            StepsSkipped = skipped,
            Total = definition.Steps.Count,
            StepLogs = ctx.StepLogs,
        };
    }

    private bool EvaluateCondition(PipelineExecutionContext ctx, ConditionDefinition condition)
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

        return evaluator.Evaluate(ctx, condition);
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
            _logger.LogWarning("Unknown action type '{Type}' — skipping", action.Type);
            return ActionResult.Failure($"Unknown action type '{action.Type}'");
        }

        return await executor.ExecuteAsync(ctx, action);
    }
}

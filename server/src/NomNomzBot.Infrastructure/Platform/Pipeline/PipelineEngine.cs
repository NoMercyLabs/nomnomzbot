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

    // Tree-execution safety caps (pipeline-control-flow.md D6, pipeline-tree-and-editor.md §2.4/§2.6).
    // A block-nesting depth beyond this or a loop that would iterate past this aborts the RUN cleanly
    // with a recorded reason — a live-stream safety property, never a hang or a stack overflow.
    private const int MaxRecursionDepth = 8;
    private const int MaxLoopIterations = 1000;

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

    /// <summary>Weighted random_branch case pick (pipeline-control-flow.md D5). Defaults to the shared
    /// CSPRNG; overridable only by tests, so a weighted-distribution test can seed deterministically
    /// (the repo forbids nondeterminism in tests).</summary>
    private readonly Func<double> _randomSource;

    // Per-channel active count (separate from the CancellationTokenSources in ChannelContext).
    // Keyed by the tenant (channel) Guid.
    private readonly ConcurrentDictionary<Guid, int> _activeCount = new();

    public PipelineEngine(
        IApplicationDbContext db,
        IChannelRegistry registry,
        IEnumerable<ICommandAction> actions,
        IEnumerable<ICommandCondition> conditions,
        ILogger<PipelineEngine> logger,
        TimeProvider timeProvider,
        Func<double>? randomSource = null
    )
    {
        _db = db;
        _registry = registry;
        _actions = actions;
        _conditions = conditions;
        _logger = logger;
        _timeProvider = timeProvider;
        _randomSource = randomSource ?? Random.Shared.NextDouble;
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

        // Resolve the pipeline definition — DB steps take priority over graph JSON cache. DB steps
        // that carry any BlockKind/ParentStepId (a tree-shaped pipeline) execute via the tree walker;
        // a genuinely flat DB pipeline (every row a top-level leaf, today's shape) is translated to
        // the unchanged flat PipelineDefinition and runs through the original RunStepsAsync — zero
        // behaviour change for the owner's existing flat pipelines (pipeline-tree-and-editor.md §6).
        PipelineDefinition? definition = null;
        List<PipelineStep>? treeRows = null;
        bool isTreeRun = false;

        if (request.PipelineId.HasValue)
        {
            treeRows = await LoadStepRowsAsync(request.PipelineId.Value, ct);
            if (treeRows.Count > 0)
            {
                isTreeRun = treeRows.Any(r =>
                    r.BlockKind is not null || r.ParentStepId is not null
                );
                if (!isTreeRun)
                    definition = BuildFlatDefinition(treeRows);
            }
            else
            {
                // Fall back to graph JSON cache if DB steps are absent.
                definition = ParseJson(request.PipelineJson);
            }
        }
        else
        {
            definition = ParseJson(request.PipelineJson);
        }

        if (!isTreeRun && definition is null)
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

        int totalLeaves = isTreeRun
            ? treeRows!.Count(r => r.BlockKind is null)
            : definition!.Steps.Count;
        if (totalLeaves == 0)
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
            result = isTreeRun
                ? await RunTreeAsync(execCtx, treeRows!, startedAt, linkedCts.Token)
                : await RunStepsAsync(execCtx, definition!, startedAt, linkedCts.Token);
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
                Total = totalLeaves,
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
                Total = totalLeaves,
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

    /// <summary>
    /// Loads every enabled <see cref="PipelineStep"/> row for a pipeline (any depth), with its
    /// condition-tree rows, ordered by <c>Order</c>. The caller decides tree-vs-flat from the shape
    /// (<see cref="PipelineStep.BlockKind"/>/<see cref="PipelineStep.ParentStepId"/>).
    /// </summary>
    private async Task<List<PipelineStep>> LoadStepRowsAsync(
        Guid pipelineId,
        CancellationToken ct
    ) =>
        await _db
            .PipelineSteps.Include(s => s.Conditions)
            .Where(s => s.PipelineId == pipelineId && s.IsEnabled)
            .OrderBy(s => s.Order)
            .ToListAsync(ct);

    /// <summary>
    /// Translates a genuinely flat set of DB rows (no <see cref="PipelineStep.BlockKind"/>, no
    /// <see cref="PipelineStep.ParentStepId"/> — today's shape) into the legacy <see cref="PipelineDefinition"/>
    /// so it executes through the unmodified <see cref="RunStepsAsync"/> — zero behaviour change.
    /// </summary>
    private static PipelineDefinition BuildFlatDefinition(List<PipelineStep> rows)
    {
        PipelineDefinition definition = new();
        foreach (PipelineStep row in rows.OrderBy(r => r.Order))
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
                continue;

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

    // ─── Tree execution (pipeline-control-flow.md D1-D6, pipeline-tree-and-editor.md §2) ─────

    private async Task<PipelineExecutionResult> RunTreeAsync(
        PipelineExecutionContext ctx,
        List<PipelineStep> rows,
        DateTimeOffset startedAt,
        CancellationToken ct
    )
    {
        List<PipelineTreeNode> roots = BuildTree(rows);
        PipelineTreeRunState state = new();

        await ExecuteNodesAsync(roots, ctx, state, depth: 0, ct);

        PipelineOutcome outcome =
            state.AbortedBudget ? PipelineOutcome.AbortedBudget
            : state.FailedBreak ? PipelineOutcome.PartiallyFailed
            : state.StoppedDeliberately ? PipelineOutcome.Stopped
            : PipelineOutcome.Completed;

        return new()
        {
            ExecutionId = ctx.ExecutionId,
            Outcome = outcome,
            Duration = _timeProvider.GetUtcNow() - startedAt,
            StepsExecuted = state.Executed,
            StepsSkipped = state.Skipped,
            Total = rows.Count(r => r.BlockKind is null),
            StepLogs = ctx.StepLogs,
            ErrorMessage = state.AbortReason,
        };
    }

    private static List<PipelineTreeNode> BuildTree(List<PipelineStep> rows)
    {
        ILookup<Guid?, PipelineStep> byParent = rows.ToLookup(r => r.ParentStepId);
        return BuildLevel(null, byParent);
    }

    private static List<PipelineTreeNode> BuildLevel(
        Guid? parentId,
        ILookup<Guid?, PipelineStep> byParent
    )
    {
        return
        [
            .. byParent[parentId]
                .OrderBy(s => s.Order)
                .Select(s => new PipelineTreeNode
                {
                    Step = s,
                    Children = BuildLevel(s.Id, byParent),
                }),
        ];
    }

    /// <summary>Walks one list of sibling nodes depth-first, stopping as soon as any of the run's
    /// terminal flags (budget/failure/deliberate-stop) is set — by this walk or one it bubbled up
    /// from. <paramref name="depth"/> is the block-nesting depth; exceeding <see cref="MaxRecursionDepth"/>
    /// aborts the run cleanly instead of recursing further (pipeline-tree-and-editor.md §2.6).</summary>
    private async Task ExecuteNodesAsync(
        List<PipelineTreeNode> nodes,
        PipelineExecutionContext ctx,
        PipelineTreeRunState state,
        int depth,
        CancellationToken ct
    )
    {
        if (depth > MaxRecursionDepth)
        {
            state.AbortedBudget = true;
            state.AbortReason = "max_recursion_depth_exceeded";
            return;
        }

        foreach (PipelineTreeNode node in nodes)
        {
            if (state.AbortedBudget || state.FailedBreak || state.StoppedDeliberately)
                return;

            ct.ThrowIfCancellationRequested();
            await ExecuteNodeAsync(node, ctx, state, depth, ct);
        }
    }

    private async Task ExecuteNodeAsync(
        PipelineTreeNode node,
        PipelineExecutionContext ctx,
        PipelineTreeRunState state,
        int depth,
        CancellationToken ct
    )
    {
        PipelineStep step = node.Step;
        switch (step.BlockKind)
        {
            case null:
                await ExecuteLeafAsync(step, ctx, state, ct);
                break;

            case "if":
                bool matched = await EvaluateConditionTreeAsync(ctx, step.Conditions);
                List<PipelineTreeNode> arm =
                [
                    .. node.Children.Where(c => c.Step.Branch == (matched ? "then" : "else")),
                ];
                await ExecuteNodesAsync(arm, ctx, state, depth + 1, ct);
                break;

            case "switch":
                await ExecuteSwitchAsync(node, ctx, state, depth, ct);
                break;

            case "loop":
                await ExecuteLoopAsync(node, ctx, state, depth, ct);
                break;

            case "random_branch":
                await ExecuteRandomBranchAsync(node, ctx, state, depth, ct);
                break;

            case "try":
                await ExecuteTryAsync(node, ctx, state, depth, ct);
                break;

            case "detached_step":
                ExecuteDetachedStep(node, ctx);
                break;

            default:
                // Fail-closed: an unrecognized block kind aborts the run, same posture as an
                // unknown action/condition type.
                _logger.LogError("Unknown block kind '{BlockKind}' — fail-closed", step.BlockKind);
                ctx.StepLogs.Add(
                    new()
                    {
                        StepIndex = ctx.StepLogs.Count,
                        ActionType = $"block:{step.BlockKind}",
                        Succeeded = false,
                        Duration = TimeSpan.Zero,
                        ErrorMessage = $"Unknown block kind '{step.BlockKind}'",
                    }
                );
                state.FailedBreak = true;
                break;
        }
    }

    private async Task ExecuteLeafAsync(
        PipelineStep step,
        PipelineExecutionContext ctx,
        PipelineTreeRunState state,
        CancellationToken ct
    )
    {
        int stepIndex = ctx.StepLogs.Count;
        DateTimeOffset stepStart = _timeProvider.GetUtcNow();

        if (step.Conditions.Count > 0 && !await EvaluateConditionTreeAsync(ctx, step.Conditions))
        {
            state.Skipped++;
            ctx.StepLogs.Add(
                new()
                {
                    StepIndex = stepIndex,
                    ActionType = step.ActionType,
                    Succeeded = true,
                    Duration = _timeProvider.GetUtcNow() - stepStart,
                    Output = "Condition not met — step skipped",
                }
            );
            return;
        }

        ActionDefinition? action;
        try
        {
            action = JsonSerializer.Deserialize<ActionDefinition>(step.ConfigJson, JsonOpts);
        }
        catch
        {
            action = null;
        }

        if (action is null)
        {
            ctx.StepLogs.Add(
                new()
                {
                    StepIndex = stepIndex,
                    ActionType = step.ActionType,
                    Succeeded = false,
                    Duration = _timeProvider.GetUtcNow() - stepStart,
                    ErrorMessage = "Invalid ConfigJson",
                }
            );
            state.FailedBreak = true;
            return;
        }

        action.Type = step.ActionType;

        ActionResult actionResult;
        try
        {
            actionResult = await ExecuteActionAsync(ctx, action, ct);
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
                action.Type,
                stepIndex
            );
            ctx.StepLogs.Add(
                new()
                {
                    StepIndex = stepIndex,
                    ActionType = action.Type,
                    Succeeded = false,
                    Duration = _timeProvider.GetUtcNow() - stepStart,
                    ErrorMessage = ex.Message,
                }
            );
            state.FailedBreak = true;
            return;
        }

        ctx.StepLogs.Add(
            new()
            {
                StepIndex = stepIndex,
                ActionType = action.Type,
                Succeeded = actionResult.Succeeded,
                Duration = _timeProvider.GetUtcNow() - stepStart,
                Output = actionResult.Output,
                ErrorMessage = actionResult.ErrorMessage,
            }
        );

        ctx.Variables["last.success"] = actionResult.Succeeded ? "true" : "false";
        ctx.Variables["last.output"] = actionResult.Output ?? string.Empty;
        ctx.Variables["last.error"] = actionResult.ErrorMessage ?? string.Empty;

        if (actionResult.Succeeded)
        {
            state.Executed++;
        }
        else
        {
            state.FailedBreak = true;
            return;
        }

        if (ctx.ShouldStop)
            state.StoppedDeliberately = true;
    }

    /// <summary>Evaluates the condition TREE guarding a step or an <c>if</c>/<c>while</c> block
    /// (pipeline-tree-and-editor.md §1.2/E2): post-order, group nodes combine children by
    /// <c>GroupOp</c> (and/or), each node's result is inverted by its own <c>Negate</c>. Zero rows
    /// means "always true" (unchanged from today's no-condition step). A flat legacy row set (every
    /// row's <c>ParentConditionId</c> null, no <c>GroupOp</c>) evaluates as an implicit AND list,
    /// same meaning as before — this is the one path that ALSO honors each leaf's <c>Negate</c>,
    /// which the legacy single-condition translation never wired up (a pre-existing gap outside this
    /// slice's flat-pipeline regression contract, fixed only for tree-shaped runs going forward).</summary>
    private async Task<bool> EvaluateConditionTreeAsync(
        PipelineExecutionContext ctx,
        ICollection<PipelineStepCondition> conditions
    )
    {
        if (conditions.Count == 0)
            return true;

        ILookup<Guid?, PipelineStepCondition> byParent = conditions.ToLookup(c =>
            c.ParentConditionId
        );

        List<PipelineStepCondition> roots = [.. byParent[null].OrderBy(c => c.Order)];
        if (roots.Count == 0)
            return true;

        bool treeShaped =
            roots.Any(r => r.GroupOp is not null)
            || conditions.Any(c => c.ParentConditionId is not null);
        if (!treeShaped)
        {
            foreach (PipelineStepCondition leaf in roots)
            {
                bool leafResult = await EvaluateConditionAsync(ctx, ToConditionDefinition(leaf));
                if (leaf.Negate)
                    leafResult = !leafResult;
                if (!leafResult)
                    return false;
            }
            return true;
        }

        foreach (PipelineStepCondition root in roots)
        {
            if (!await EvaluateConditionTreeNodeAsync(ctx, root, byParent))
                return false;
        }
        return true;
    }

    private async Task<bool> EvaluateConditionTreeNodeAsync(
        PipelineExecutionContext ctx,
        PipelineStepCondition node,
        ILookup<Guid?, PipelineStepCondition> byParent
    )
    {
        bool result;
        if (node.GroupOp is not null)
        {
            List<PipelineStepCondition> children = [.. byParent[node.Id].OrderBy(c => c.Order)];
            if (string.Equals(node.GroupOp, "or", StringComparison.OrdinalIgnoreCase))
            {
                result = false;
                foreach (PipelineStepCondition child in children)
                {
                    if (await EvaluateConditionTreeNodeAsync(ctx, child, byParent))
                    {
                        result = true;
                        break;
                    }
                }
            }
            else
            {
                result = true;
                foreach (PipelineStepCondition child in children)
                {
                    if (!await EvaluateConditionTreeNodeAsync(ctx, child, byParent))
                    {
                        result = false;
                        break;
                    }
                }
            }
        }
        else
        {
            result = await EvaluateConditionAsync(ctx, ToConditionDefinition(node));
        }

        return node.Negate ? !result : result;
    }

    private static ConditionDefinition ToConditionDefinition(PipelineStepCondition row) =>
        new()
        {
            Type = row.ConditionType,
            Parameters = new()
            {
                ["operator"] = JsonSerializer.SerializeToElement(row.Operator ?? "eq", JsonOpts),
                ["left"] = JsonSerializer.SerializeToElement(
                    row.LeftOperand ?? string.Empty,
                    JsonOpts
                ),
                ["right"] = JsonSerializer.SerializeToElement(
                    row.RightOperand ?? string.Empty,
                    JsonOpts
                ),
            },
        };

    private async Task ExecuteSwitchAsync(
        PipelineTreeNode node,
        PipelineExecutionContext ctx,
        PipelineTreeRunState state,
        int depth,
        CancellationToken ct
    )
    {
        SwitchBlockConfig? config = ParseBlockConfig<SwitchBlockConfig>(node.Step.BlockConfigJson);
        string switchValue = ResolveScalar(config?.Value, ctx);

        List<PipelineTreeNode> cases =
        [
            .. node
                .Children.Where(c => c.Step.BlockKind == "switch_case")
                .OrderBy(c => c.Step.Order),
        ];

        PipelineTreeNode? matched = null;
        PipelineTreeNode? defaultCase = null;
        foreach (PipelineTreeNode candidate in cases)
        {
            SwitchCaseBlockConfig? caseConfig = ParseBlockConfig<SwitchCaseBlockConfig>(
                candidate.Step.BlockConfigJson
            );
            if (caseConfig?.IsDefault == true)
            {
                defaultCase ??= candidate;
                continue;
            }

            if (MatchesCase(switchValue, caseConfig))
            {
                matched = candidate;
                break;
            }
        }

        matched ??= defaultCase;
        if (matched is not null)
            await ExecuteNodesAsync(matched.Children, ctx, state, depth + 1, ct);
    }

    private static bool MatchesCase(string switchValue, SwitchCaseBlockConfig? config)
    {
        if (config is null)
            return false;

        string match = config.Match ?? string.Empty;
        string op = (config.Operator ?? "eq").Trim().ToLowerInvariant();

        bool leftIsNumeric = double.TryParse(switchValue, out double leftNum);
        bool rightIsNumeric = double.TryParse(match, out double rightNum);
        int comparison =
            leftIsNumeric && rightIsNumeric
                ? leftNum.CompareTo(rightNum)
                : string.Compare(switchValue, match, StringComparison.OrdinalIgnoreCase);

        return op switch
        {
            "eq" or "==" => comparison == 0,
            "ne" or "!=" => comparison != 0,
            "gt" or ">" => comparison > 0,
            "lt" or "<" => comparison < 0,
            "gte" or ">=" => comparison >= 0,
            "lte" or "<=" => comparison <= 0,
            "contains" => switchValue.Contains(match, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    /// <summary>Resolves a scalar block-config field: <c>{{name}}</c> or a bare <c>name</c> is looked
    /// up in the run's variable bag; anything not found falls back to the literal text itself, so an
    /// author can switch/foreach on either a variable or a constant.</summary>
    private static string ResolveScalar(string? raw, PipelineExecutionContext ctx)
    {
        if (string.IsNullOrEmpty(raw))
            return string.Empty;

        string key =
            raw.StartsWith("{{", StringComparison.Ordinal)
            && raw.EndsWith("}}", StringComparison.Ordinal)
                ? raw[2..^2].Trim()
                : raw;

        return ctx.Variables.GetValueOrDefault(key, raw);
    }

    private async Task ExecuteLoopAsync(
        PipelineTreeNode node,
        PipelineExecutionContext ctx,
        PipelineTreeRunState state,
        int depth,
        CancellationToken ct
    )
    {
        LoopBlockConfig config =
            ParseBlockConfig<LoopBlockConfig>(node.Step.BlockConfigJson) ?? new();
        string mode = (config.Mode ?? "repeat").ToLowerInvariant();
        int cap = Math.Clamp(config.MaxIterations ?? MaxLoopIterations, 1, MaxLoopIterations);

        List<string> items = [];
        if (mode == "foreach")
        {
            string raw = ResolveScalar(config.ListVar, ctx);
            items =
            [
                .. raw.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                ),
            ];
        }

        System.Diagnostics.Stopwatch? loopClock = config.MaxLoopRuntimeSeconds.HasValue
            ? System.Diagnostics.Stopwatch.StartNew()
            : null;

        int index = 0;
        string previousItem = string.Empty;

        while (true)
        {
            if (state.AbortedBudget || state.FailedBreak || state.StoppedDeliberately)
                return;
            ct.ThrowIfCancellationRequested();

            bool shouldContinue = mode switch
            {
                "foreach" => index < items.Count,
                "while" => await EvaluateConditionTreeAsync(ctx, node.Step.Conditions),
                _ => index < (config.Count ?? 0),
            };
            if (!shouldContinue)
                return;

            if (index >= cap)
            {
                state.AbortedBudget = true;
                state.AbortReason = "loop_iteration_cap_exceeded";
                return;
            }

            if (
                loopClock is not null
                && loopClock.Elapsed.TotalSeconds > config.MaxLoopRuntimeSeconds!.Value
            )
            {
                state.AbortedBudget = true;
                state.AbortReason = "loop_runtime_exceeded";
                return;
            }

            string currentItem = mode == "foreach" ? items[index] : index.ToString();
            ctx.Variables["loop.index"] = index.ToString();
            ctx.Variables["loop.item"] = currentItem;
            ctx.Variables["loop.previous_item"] = previousItem;
            ctx.Variables["loop.count"] =
                mode == "foreach" ? items.Count.ToString() : (config.Count ?? 0).ToString();

            PipelineTreeRunState iterationState = new();
            await ExecuteNodesAsync(node.Children, ctx, iterationState, depth + 1, ct);

            state.Executed += iterationState.Executed;
            state.Skipped += iterationState.Skipped;

            if (iterationState.AbortedBudget)
            {
                state.AbortedBudget = true;
                state.AbortReason = iterationState.AbortReason;
                return;
            }
            if (iterationState.FailedBreak)
            {
                state.FailedBreak = true;
                return;
            }
            if (iterationState.StoppedDeliberately)
            {
                state.StoppedDeliberately = true;
                return;
            }

            previousItem = currentItem;
            index++;
        }
    }

    private async Task ExecuteRandomBranchAsync(
        PipelineTreeNode node,
        PipelineExecutionContext ctx,
        PipelineTreeRunState state,
        int depth,
        CancellationToken ct
    )
    {
        List<PipelineTreeNode> cases =
        [
            .. node
                .Children.Where(c => c.Step.BlockKind == "random_case")
                .OrderBy(c => c.Step.Order),
        ];
        if (cases.Count == 0)
            return;

        List<(PipelineTreeNode Node, decimal Weight)> weighted =
        [
            .. cases.Select(c =>
                (c, ParseBlockConfig<RandomCaseBlockConfig>(c.Step.BlockConfigJson)?.Weight ?? 1m)
            ),
        ];
        decimal total = weighted.Sum(w => w.Weight);
        if (total <= 0)
            return;

        double roll = _randomSource() * (double)total;
        decimal cumulative = 0;
        PipelineTreeNode? chosen = null;
        foreach ((PipelineTreeNode candidate, decimal weight) in weighted)
        {
            cumulative += weight;
            if ((double)cumulative >= roll)
            {
                chosen = candidate;
                break;
            }
        }
        chosen ??= weighted[^1].Node;

        await ExecuteNodesAsync(chosen.Children, ctx, state, depth + 1, ct);
    }

    private async Task ExecuteTryAsync(
        PipelineTreeNode node,
        PipelineExecutionContext ctx,
        PipelineTreeRunState state,
        int depth,
        CancellationToken ct
    )
    {
        List<PipelineTreeNode> body = [.. node.Children.Where(c => c.Step.Branch == "then")];
        List<PipelineTreeNode> catchChildren =
        [
            .. node.Children.Where(c => c.Step.Branch == "else"),
        ];

        PipelineTreeRunState bodyState = new();
        await ExecuteNodesAsync(body, ctx, bodyState, depth + 1, ct);

        state.Executed += bodyState.Executed;
        state.Skipped += bodyState.Skipped;

        if (bodyState.AbortedBudget)
        {
            // A budget breach is never swallowed — every cap breach aborts the run cleanly
            // regardless of which block it tripped inside (pipeline-control-flow.md D6).
            state.AbortedBudget = true;
            state.AbortReason = bodyState.AbortReason;
            return;
        }

        if (bodyState.FailedBreak)
        {
            PipelineTreeRunState catchState = new();
            await ExecuteNodesAsync(catchChildren, ctx, catchState, depth + 1, ct);

            state.Executed += catchState.Executed;
            state.Skipped += catchState.Skipped;

            if (catchState.AbortedBudget)
            {
                state.AbortedBudget = true;
                state.AbortReason = catchState.AbortReason;
                return;
            }
            if (catchState.FailedBreak)
            {
                // The catch handler's own failure is not further caught — propagates normally.
                state.FailedBreak = true;
                return;
            }
            if (catchState.StoppedDeliberately)
                state.StoppedDeliberately = true;

            // Failure caught and (optionally) handled — continue past the try block.
            return;
        }

        if (bodyState.StoppedDeliberately)
            state.StoppedDeliberately = true;
    }

    private void ExecuteDetachedStep(PipelineTreeNode node, PipelineExecutionContext ctx)
    {
        PipelineTreeNode? leaf = node.Children.FirstOrDefault(c => c.Step.BlockKind is null);
        if (leaf is null)
            return;

        ActionDefinition? action;
        try
        {
            action = JsonSerializer.Deserialize<ActionDefinition>(leaf.Step.ConfigJson, JsonOpts);
        }
        catch
        {
            action = null;
        }
        if (action is null)
            return;
        action.Type = leaf.Step.ActionType;

        // Fire-and-forget alongside the main chain: dispatched now, never awaited by the parent run,
        // and its own failure never fails the parent (pipeline-tree-and-editor.md §1.1/§2.1).
        _ = Task.Run(async () =>
        {
            try
            {
                await ExecuteActionAsync(ctx, action, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Detached pipeline step {ActionType} failed", action.Type);
            }
        });
    }

    private static T? ParseBlockConfig<T>(string? json)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        }
        catch
        {
            return null;
        }
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

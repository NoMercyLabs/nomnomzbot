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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
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
    private readonly ITemplateResolver _templateResolver;
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
        ITemplateResolver templateResolver,
        ILogger<PipelineEngine> logger,
        TimeProvider timeProvider,
        Func<double>? randomSource = null
    )
    {
        _db = db;
        _registry = registry;
        _actions = actions;
        _conditions = conditions;
        _templateResolver = templateResolver;
        _logger = logger;
        _timeProvider = timeProvider;
        _randomSource = randomSource ?? Random.Shared.NextDouble;
    }

    public int GetActiveCountForChannel(Guid broadcasterId) =>
        _activeCount.GetValueOrDefault(broadcasterId, 0);

    public async Task CancelAllForChannelAsync(Guid broadcasterId)
    {
        ChannelContext? ctx = _registry.Get(broadcasterId);
        if (ctx is not null)
        {
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
        }

        // A SUSPENDED run has no live CancellationTokenSource to cancel — its in-process task already
        // finished (with Outcome=Suspended) long before this channel went offline, so it is never in
        // ChannelContext.ActivePipelines and this step must run whether or not the registry even has a
        // live ChannelContext for this broadcaster. Left alone it would strand forever, still
        // "suspended" and still eligible for a future wait_for_event match on a stream that isn't live
        // anymore (S-PIPE-TREE-d3b REQUIRED #4). Recorded, never deleted: a cancelled row stays
        // queryable so the streamer can see why their command never finished. Loaded and mutated
        // through the change tracker (not ExecuteUpdateAsync) so a caller holding the SAME tracked
        // instance — as every test and the persist-then-cancel-in-one-request path does — observes the
        // cancellation immediately, not a stale identity-mapped copy.
        List<PipelineRunState> suspendedRuns = await _db
            .PipelineRunStates.Where(r =>
                r.BroadcasterId == broadcasterId && r.Status == "suspended"
            )
            .ToListAsync();
        DateTimeOffset cancelledAt = _timeProvider.GetUtcNow();
        foreach (PipelineRunState suspendedRun in suspendedRuns)
        {
            suspendedRun.Status = "cancelled";
            suspendedRun.CompletedAt = cancelledAt;
        }
        if (suspendedRuns.Count > 0)
            await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Cancelled all pipelines for channel {BroadcasterId} ({SuspendedCount} suspended run(s) also cancelled)",
            broadcasterId,
            suspendedRuns.Count
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

        // A suspended run gets its own row (PipelineRunState) — persisted BEFORE the telemetry write
        // below so a cap breach can still downgrade the outcome to Failed on the SAME telemetry row,
        // never silently drop the run (S-PIPE-TREE-d3a REQUIRED #3).
        if (result.Outcome == PipelineOutcome.Suspended)
            result = await PersistSuspendedRunAsync(request, execCtx, result, ct);

        // Persist outside the finally block: SaveChangesAsync can itself throw (e.g. transient DB
        // failure) and must not mask the pipeline's own outcome above, nor prevent the active-count
        // decrement in `finally` from running first.
        await PersistExecutionAsync(request, definition, startedAt, result, ct);
        return result;
    }

    // ─── Suspend / resume persistence core (S-PIPE-TREE-d3a) ─────────────────

    /// <summary>Per-channel cap on concurrently SUSPENDED runs — distinct from
    /// <see cref="MaxConcurrentPerChannel"/> (actively running). A spammer could otherwise pile up an
    /// unbounded number of parked runs; exceeding this fails the run honestly (Outcome=Failed with a
    /// named reason) rather than silently dropping it.</summary>
    private const int MaxSuspendedRunsPerChannel = 50;

    private async Task<PipelineExecutionResult> PersistSuspendedRunAsync(
        PipelineRequest request,
        PipelineExecutionContext execCtx,
        PipelineExecutionResult result,
        CancellationToken ct
    )
    {
        int suspendedCount = await _db.PipelineRunStates.CountAsync(
            r => r.BroadcasterId == request.BroadcasterId && r.Status == "suspended",
            ct
        );
        if (suspendedCount >= MaxSuspendedRunsPerChannel)
        {
            _logger.LogWarning(
                "Channel {BroadcasterId} already has {Count} suspended pipeline runs (cap {Max}) — "
                    + "run {ExecutionId} could not be parked and failed instead",
                request.BroadcasterId,
                suspendedCount,
                MaxSuspendedRunsPerChannel,
                result.ExecutionId
            );
            return new()
            {
                ExecutionId = result.ExecutionId,
                Outcome = PipelineOutcome.Failed,
                Duration = result.Duration,
                StepsExecuted = result.StepsExecuted,
                StepsSkipped = result.StepsSkipped,
                Total = result.Total,
                StepLogs = result.StepLogs,
                ErrorMessage =
                    $"suspended_run_cap_exceeded: channel already has {MaxSuspendedRunsPerChannel} suspended pipeline runs",
            };
        }

        Guid runStateId = Guid.NewGuid();
        Guid triggeredByUserId = Guid.TryParse(request.TriggeredByUserId, out Guid parsed)
            ? parsed
            : Guid.Empty;

        PipelineRunState runState = new()
        {
            Id = runStateId,
            BroadcasterId = request.BroadcasterId,
            PipelineId = request.PipelineId ?? Guid.Empty,
            Status = "suspended",
            SuspendedAtStepId = result.SuspendedAtStepId,
            VariablesJson = JsonSerializer.Serialize(execCtx.Variables, JsonOpts),
            CursorJson = result.SuspendCursorJson ?? "[]",
            TriggeredByUserId = triggeredByUserId,
            TriggeredByDisplayName = request.TriggeredByDisplayName,
            // Duration already elapsed on THIS segment is the run's first slice of accumulated
            // runtime — suspended wall-clock from here on is never added to it (MaxRuntime guard).
            AccumulatedRuntimeMs = (int)result.Duration.TotalMilliseconds,
            SuspendedAt = _timeProvider.GetUtcNow(),
            WaitEventName = result.SuspendWaitEventName,
            WaitTimeoutAt = result.SuspendWaitTimeoutSeconds is int secs
                ? _timeProvider.GetUtcNow().AddSeconds(secs)
                : null,
        };

        _db.PipelineRunStates.Add(runState);
        await _db.SaveChangesAsync(ct);

        return new()
        {
            ExecutionId = result.ExecutionId,
            Outcome = PipelineOutcome.Suspended,
            Duration = result.Duration,
            StepsExecuted = result.StepsExecuted,
            StepsSkipped = result.StepsSkipped,
            Total = result.Total,
            StepLogs = result.StepLogs,
            SuspendedAtStepId = result.SuspendedAtStepId,
            SuspendCursorJson = result.SuspendCursorJson,
            SuspendedRunStateId = runStateId,
        };
    }

    public Task<PipelineExecutionResult> ResumeAsync(
        Guid runStateId,
        CancellationToken ct = default
    ) => ResumeInternalAsync(runStateId, extraVariables: null, ct);

    /// <summary>Shared resume path for a plain restart-resume (<see cref="ResumeAsync"/>), an
    /// event-matched resume (<see cref="ResumeSuspendedRunsForEventAsync"/>), and a timeout resume
    /// (<see cref="ResumeTimedOutWaitsAsync"/>) — <paramref name="extraVariables"/> is merged into the
    /// restored variable bag AFTER the persisted ones (S-PIPE-TREE-d3b), so an event's data / the
    /// timeout markers are visible to every step from here on, exactly like any other run variable.</summary>
    private async Task<PipelineExecutionResult> ResumeInternalAsync(
        Guid runStateId,
        IReadOnlyDictionary<string, string>? extraVariables,
        CancellationToken ct
    )
    {
        PipelineRunState? runState = await _db.PipelineRunStates.FirstOrDefaultAsync(
            r => r.Id == runStateId && r.Status == "suspended",
            ct
        );
        if (runState is null)
            return new()
            {
                ExecutionId = runStateId.ToString("N")[..12],
                Outcome = PipelineOutcome.Failed,
                Duration = TimeSpan.Zero,
                ErrorMessage = "pipeline_run_state_not_found",
            };

        DateTimeOffset startedAt = _timeProvider.GetUtcNow();

        // MaxRuntime excludes suspended wall-clock: only time already spent ACTUALLY RUNNING (never
        // the interval a run sat parked) counts against the budget — a run paused for an hour has not
        // "run" for an hour (settled CTO decision). A run that had already burned its whole budget
        // across earlier segments is timed out immediately, before touching a single step.
        TimeSpan remaining =
            ExecutionTimeout - TimeSpan.FromMilliseconds(runState.AccumulatedRuntimeMs);
        if (remaining <= TimeSpan.Zero)
        {
            runState.Status = "failed";
            runState.CompletedAt = startedAt;
            await _db.SaveChangesAsync(ct);
            return new()
            {
                ExecutionId = runStateId.ToString("N")[..12],
                Outcome = PipelineOutcome.TimedOut,
                Duration = TimeSpan.Zero,
                ErrorMessage = "max_runtime_exceeded",
            };
        }

        List<PipelineStep> rows = await LoadStepRowsAsync(runState.PipelineId, ct);

        Dictionary<string, string> variables =
            JsonSerializer.Deserialize<Dictionary<string, string>>(runState.VariablesJson, JsonOpts)
            ?? [];
        List<PipelineRunFrame> cursorPath =
            JsonSerializer.Deserialize<List<PipelineRunFrame>>(runState.CursorJson, JsonOpts) ?? [];

        PipelineExecutionContext execCtx = new()
        {
            BroadcasterId = runState.BroadcasterId,
            TriggeredByUserId = runState.TriggeredByUserId.ToString(),
            TriggeredByDisplayName = runState.TriggeredByDisplayName,
            MessageId = string.Empty,
            RawMessage = string.Empty,
            CancellationToken = ct,
        };
        foreach ((string k, string v) in variables)
            execCtx.Variables[k] = v;
        if (extraVariables is not null)
            foreach ((string k, string v) in extraVariables)
                execCtx.Variables[k] = v;

        // No SuspendedAtStepId means nothing had run yet when this row was persisted (or the leaf it
        // suspended at was the very first thing at the top level with an empty cursor path) — either
        // way there is nothing to relocate past, so resume with no cursor at all rather than trying to
        // match a step id that was never recorded.
        PipelineResumeCursor? resume = runState.SuspendedAtStepId is { } suspendedLeafId
            ? new()
            {
                Path = cursorPath,
                Index = 0,
                SuspendedLeafStepId = suspendedLeafId,
            }
            : null;

        using CancellationTokenSource timeoutCts = new(remaining);
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            ct,
            timeoutCts.Token
        );

        PipelineExecutionResult result;
        try
        {
            result = await RunTreeAsync(execCtx, rows, startedAt, linkedCts.Token, resume);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            result = new()
            {
                ExecutionId = execCtx.ExecutionId,
                Outcome = PipelineOutcome.TimedOut,
                Duration = _timeProvider.GetUtcNow() - startedAt,
                StepLogs = execCtx.StepLogs,
            };
        }

        int segmentMs = (int)(_timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
        runState.AccumulatedRuntimeMs += segmentMs;

        if (result.Outcome == PipelineOutcome.Suspended)
        {
            runState.SuspendedAtStepId = result.SuspendedAtStepId;
            runState.VariablesJson = JsonSerializer.Serialize(execCtx.Variables, JsonOpts);
            runState.CursorJson = result.SuspendCursorJson ?? "[]";
            runState.SuspendedAt = _timeProvider.GetUtcNow();
            // A resumed run may itself suspend again on a LATER wait_for_event step — re-derive the
            // wait fields from THIS segment's result rather than leaving the prior wait's values
            // stale, and clear them entirely when the new suspension wasn't a wait (e.g. a nested
            // sub-pipeline suspending on the caller's behalf with no event name of its own).
            runState.WaitEventName = result.SuspendWaitEventName;
            runState.WaitTimeoutAt = result.SuspendWaitTimeoutSeconds is int secs
                ? _timeProvider.GetUtcNow().AddSeconds(secs)
                : null;
            result = new()
            {
                ExecutionId = result.ExecutionId,
                Outcome = result.Outcome,
                Duration = result.Duration,
                StepsExecuted = result.StepsExecuted,
                StepsSkipped = result.StepsSkipped,
                Total = result.Total,
                StepLogs = result.StepLogs,
                SuspendedAtStepId = result.SuspendedAtStepId,
                SuspendCursorJson = result.SuspendCursorJson,
                SuspendedRunStateId = runState.Id,
                SuspendWaitEventName = result.SuspendWaitEventName,
                SuspendWaitTimeoutSeconds = result.SuspendWaitTimeoutSeconds,
            };
        }
        else
        {
            runState.Status = result.Outcome switch
            {
                PipelineOutcome.Completed or PipelineOutcome.Stopped => "completed",
                PipelineOutcome.Cancelled => "cancelled",
                _ => "failed",
            };
            runState.CompletedAt = _timeProvider.GetUtcNow();
            runState.WaitEventName = null;
            runState.WaitTimeoutAt = null;
        }

        runState.ResumedAt = startedAt;
        await _db.SaveChangesAsync(ct);

        return result;
    }

    // ─── Event-matched resume / timeout sweep (S-PIPE-TREE-d3b) ───────────────

    public async Task<int> ResumeSuspendedRunsForEventAsync(
        Guid broadcasterId,
        string eventName,
        IReadOnlyDictionary<string, string> eventData,
        CancellationToken ct = default
    )
    {
        // Snapshot the matching ids FIRST, then resume one at a time — resuming mutates/removes rows
        // from the "suspended" set as it goes, so a single streaming query would see a moving target.
        // Case-insensitive on the event name: an author typing "SongRequested" must still match a
        // waiter configured with "songrequested" — case is not semantically meaningful here.
        List<Guid> matchingRunStateIds = await _db
            .PipelineRunStates.Where(r =>
                r.BroadcasterId == broadcasterId
                && r.Status == "suspended"
                && r.WaitEventName != null
                && r.WaitEventName.ToLower() == eventName.ToLower()
            )
            .Select(r => r.Id)
            .ToListAsync(ct);

        Dictionary<string, string> merged = new(StringComparer.OrdinalIgnoreCase)
        {
            ["event.name"] = eventName,
            ["event.matched"] = "true",
            ["event.timed_out"] = "false",
        };
        foreach ((string k, string v) in eventData)
            merged[$"event.{k}"] = v;

        int resumed = 0;
        foreach (Guid runStateId in matchingRunStateIds)
        {
            await ResumeInternalAsync(runStateId, merged, ct);
            resumed++;
        }
        return resumed;
    }

    public async Task<int> ResumeTimedOutWaitsAsync(CancellationToken ct = default)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        // The deadline comparison (WaitTimeoutAt <= now) is evaluated client-side: the SQLite provider
        // cannot translate a nullable DateTimeOffset "<=" comparison against a captured variable into
        // SQL. The server-side filter (status + non-null deadline) still keeps the candidate set small
        // — only channels with an actual pending wait are ever pulled into memory.
        List<PipelineRunState> candidates = await _db
            .PipelineRunStates.Where(r => r.Status == "suspended" && r.WaitTimeoutAt != null)
            .ToListAsync(ct);
        List<PipelineRunState> expired = [.. candidates.Where(r => r.WaitTimeoutAt <= now)];

        int resumed = 0;
        foreach (PipelineRunState expiredRun in expired)
        {
            Guid runStateId = expiredRun.Id;
            string? eventName = expiredRun.WaitEventName;
            Dictionary<string, string> timeoutVariables = new(StringComparer.OrdinalIgnoreCase)
            {
                ["event.name"] = eventName ?? string.Empty,
                ["event.matched"] = "false",
                ["event.timed_out"] = "true",
            };
            await ResumeInternalAsync(runStateId, timeoutVariables, ct);
            resumed++;
        }
        return resumed;
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

        ActionDefinition resolvedAction = await ResolveTemplatedFieldsAsync(executor, ctx, action);
        return await executor.ExecuteAsync(ctx, resolvedAction);
    }

    /// <summary>
    /// S-PIPE-TREE-d2b(b): the ONE seam every core/side-effecting action's <c>Templated</c> fields get
    /// rendered through, uniformly, before <see cref="ICommandAction.ExecuteAsync"/> ever sees them —
    /// rather than each action hand-rolling its own resolve call (the systemic gap: <c>set_variable</c>,
    /// <c>return_value</c>, and <c>run_pipeline</c>'s args stored their configured value raw, with no
    /// resolution pass at this layer for ANY action). Skipped entirely for an action whose
    /// <see cref="ICommandAction.ResolvesOwnTemplates"/> is true (e.g. <c>play_tts</c>, the chat send
    /// actions, <c>wait</c>) — those already call the resolver themselves, so running this pass too would
    /// resolve the same field twice, corrupting a literal <c>{{</c> a user typed deliberately into the
    /// FIRST resolved value. Only string- and string-array-valued parameters are touched; a field of any
    /// other JSON shape (number, bool, object) is left as-is regardless of its descriptor.
    /// </summary>
    private async Task<ActionDefinition> ResolveTemplatedFieldsAsync(
        ICommandAction executor,
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (
            executor.ResolvesOwnTemplates
            || action.Parameters is null
            || action.Parameters.Count == 0
        )
            return action;

        List<PipelineActionFieldDescriptor> templatedFields =
        [
            .. executor.Fields.Where(f => f.Templated),
        ];
        if (templatedFields.Count == 0)
            return action;

        Dictionary<string, JsonElement>? resolvedParams = null;
        foreach (PipelineActionFieldDescriptor field in templatedFields)
        {
            if (!action.Parameters.TryGetValue(field.Name, out JsonElement raw))
                continue;

            switch (raw.ValueKind)
            {
                case JsonValueKind.String:
                {
                    string template = raw.GetString() ?? string.Empty;
                    if (template.Length == 0)
                        continue;
                    string resolved = await _templateResolver.ResolveAsync(
                        template,
                        ctx.Variables,
                        ctx.BroadcasterId,
                        ctx.CancellationToken
                    );
                    resolvedParams ??= new Dictionary<string, JsonElement>(action.Parameters);
                    resolvedParams[field.Name] = JsonSerializer.SerializeToElement(resolved);
                    break;
                }
                case JsonValueKind.Array:
                {
                    JsonElement[] items = [.. raw.EnumerateArray()];
                    bool changed = false;
                    for (int i = 0; i < items.Length; i++)
                    {
                        if (items[i].ValueKind != JsonValueKind.String)
                            continue;
                        string itemTemplate = items[i].GetString() ?? string.Empty;
                        string itemResolved = await _templateResolver.ResolveAsync(
                            itemTemplate,
                            ctx.Variables,
                            ctx.BroadcasterId,
                            ctx.CancellationToken
                        );
                        items[i] = JsonSerializer.SerializeToElement(itemResolved);
                        changed = true;
                    }
                    if (changed)
                    {
                        resolvedParams ??= new Dictionary<string, JsonElement>(action.Parameters);
                        resolvedParams[field.Name] = JsonSerializer.SerializeToElement(items);
                    }
                    break;
                }
                case JsonValueKind.Object:
                {
                    // KeyValueMap fields (e.g. run_pipeline's named_args) — resolve every string-valued
                    // property, name unchanged; a non-string property value is left as-is.
                    Dictionary<string, JsonElement> objectItems = [];
                    bool objectChanged = false;
                    foreach (JsonProperty property in raw.EnumerateObject())
                    {
                        if (property.Value.ValueKind != JsonValueKind.String)
                        {
                            objectItems[property.Name] = property.Value;
                            continue;
                        }

                        string propertyTemplate = property.Value.GetString() ?? string.Empty;
                        string propertyResolved = await _templateResolver.ResolveAsync(
                            propertyTemplate,
                            ctx.Variables,
                            ctx.BroadcasterId,
                            ctx.CancellationToken
                        );
                        objectItems[property.Name] = JsonSerializer.SerializeToElement(
                            propertyResolved
                        );
                        objectChanged = true;
                    }
                    if (objectChanged)
                    {
                        resolvedParams ??= new Dictionary<string, JsonElement>(action.Parameters);
                        resolvedParams[field.Name] = JsonSerializer.SerializeToElement(objectItems);
                    }
                    break;
                }
            }
        }

        return resolvedParams is null
            ? action
            : new ActionDefinition { Type = action.Type, Parameters = resolvedParams };
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

    /// <summary>Parses <see cref="Pipeline.ParameterNamesJson"/> (S-PIPE-TREE-d2b(a)) into its
    /// declared name list. Malformed/absent JSON is treated the same as "no declared parameters" —
    /// this is a labelling aid for the editor and a named-binding allow-list, never a hard schema a
    /// bad row should be able to break a caller over.</summary>
    private static List<string> ParseParameterNames(string? parameterNamesJson)
    {
        if (string.IsNullOrWhiteSpace(parameterNamesJson))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(parameterNamesJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

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
        CancellationToken ct,
        PipelineResumeCursor? resume = null
    )
    {
        List<PipelineTreeNode> roots = BuildTree(rows);
        PipelineTreeRunState state = new();
        PipelineTreeWalk walk = new() { Resume = resume };

        await ExecuteNodesAsync(roots, ctx, state, depth: 0, ct, walk);

        PipelineOutcome outcome =
            state.SuspendRequested ? PipelineOutcome.Suspended
            : state.AbortedBudget ? PipelineOutcome.AbortedBudget
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
            SuspendedAtStepId = state.SuspendRequested ? state.SuspendStepId : null,
            SuspendCursorJson = state.SuspendRequested
                ? JsonSerializer.Serialize(walk.LivePath, JsonOpts)
                : null,
            SuspendWaitEventName = state.SuspendRequested ? state.SuspendWaitEventName : null,
            SuspendWaitTimeoutSeconds = state.SuspendRequested
                ? state.SuspendWaitTimeoutSeconds
                : null,
        };
    }

    /// <summary>
    /// <c>run_pipeline inline</c> (pipeline-control-flow.md D4, pipeline-tree-and-editor.md §2.5).
    /// Walks the target pipeline's tree using the caller's OWN <see cref="PipelineExecutionContext"/> —
    /// shared Run-scope variables, one shared <see cref="PipelineExecutionContext.CallDepth"/> counter
    /// so a call chain that crosses pipeline boundaries (A → B → A → …) is bounded exactly like a
    /// single deeply-nested pipeline would be. Uses its own child <see cref="PipelineTreeRunState"/>
    /// (the "try" shape, not the "switch" shape — pipeline-control-flow.md D3/D7): a callee's own
    /// <c>stop</c>/failure must never bleed into the caller's run state, only the pass/fail verdict
    /// and the return value returned here.
    /// </summary>
    public async Task<Result<string?>> RunInlineSubPipelineAsync(
        PipelineExecutionContext callerCtx,
        Guid targetPipelineId,
        IReadOnlyList<string>? args,
        IReadOnlyDictionary<string, string>? namedArgs = null,
        CancellationToken ct = default
    )
    {
        if (callerCtx.CallDepth >= MaxRecursionDepth)
            return Result.Failure<string?>(
                "max_recursion_depth_exceeded",
                "max_recursion_depth_exceeded"
            );

        // Tenant scoping: the callee must belong to the SAME channel as the caller — never rely
        // solely on a possibly-absent ambient tenant filter for a background/EventSub-driven run
        // (platform-conventions.md). A cross-tenant id fails closed before a single callee row loads.
        NomNomzBot.Domain.Commands.Entities.Pipeline? calleePipeline = await _db
            .Pipelines.AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.Id == targetPipelineId && p.BroadcasterId == callerCtx.BroadcasterId,
                ct
            );
        if (calleePipeline is null)
            return Result.Failure<string?>(
                "pipeline not found in this channel",
                "run_pipeline_cross_tenant"
            );

        // S-PIPE-TREE-d2b(a): named binding is only checked against declared names when the callee
        // HAS declared at least one — a callee with no declared parameters keeps accepting any
        // named/positional binding, unchanged from before this slice.
        if (namedArgs is { Count: > 0 })
        {
            List<string> declaredNames = ParseParameterNames(calleePipeline.ParameterNamesJson);
            if (declaredNames.Count > 0)
            {
                string? unknownName = namedArgs.Keys.FirstOrDefault(name =>
                    !declaredNames.Contains(name)
                );
                if (unknownName is not null)
                    return Result.Failure<string?>(
                        $"run_pipeline: '{unknownName}' is not a declared parameter of the target pipeline",
                        "run_pipeline_unknown_named_arg"
                    );
            }
        }

        List<PipelineStep> rows = await LoadStepRowsAsync(targetPipelineId, ct);
        if (rows.Count == 0)
            return Result.Success<string?>(null);

        if (args is not null)
            for (int i = 0; i < args.Count; i++)
                callerCtx.Variables[$"args.{i + 1}"] = args[i];

        // Named binding is independent of argument order in the caller's config — each entry is
        // bound straight to its own name, never derived from dictionary enumeration order.
        if (namedArgs is not null)
            foreach ((string name, string value) in namedArgs)
                callerCtx.Variables[name] = value;

        callerCtx.CallDepth++;
        string? previousReturnValue = callerCtx.ReturnValue;
        callerCtx.ReturnValue = null;
        try
        {
            List<PipelineTreeNode> calleeRoots = BuildTree(rows);
            PipelineTreeRunState calleeState = new();
            PipelineTreeWalk calleeWalk = new();
            await ExecuteNodesAsync(calleeRoots, callerCtx, calleeState, depth: 0, ct, calleeWalk);

            if (calleeState.AbortedBudget)
                return Result.Failure<string?>(
                    calleeState.AbortReason ?? "aborted_budget",
                    calleeState.AbortReason ?? "aborted_budget"
                );

            if (calleeState.FailedBreak)
                return Result.Failure<string?>(
                    "run_pipeline inline call failed",
                    "run_pipeline_callee_failed"
                );

            return Result.Success<string?>(callerCtx.ReturnValue);
        }
        finally
        {
            callerCtx.CallDepth--;
            // Restore the caller's own pending return value (if any) unless the callee set a new
            // one — an inline call that never reaches `return_value` must not clobber a return the
            // caller itself is still carrying from further up its own call chain.
            callerCtx.ReturnValue ??= previousReturnValue;

            // The callee's own `stop`/`return_value` already ended ITS tree walk (captured above as
            // calleeState.StoppedDeliberately) and must never also end the CALLER's — this run_pipeline
            // step is otherwise just another leaf. ExecuteLeafAsync never clears ShouldStop itself
            // (a flat/non-call run relies on the walk stopping right there instead), so a call boundary
            // must clear it explicitly or it leaks into the caller's own next ExecuteLeafAsync check.
            callerCtx.ShouldStop = false;
        }
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
        CancellationToken ct,
        PipelineTreeWalk walk
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
            if (
                state.AbortedBudget
                || state.FailedBreak
                || state.StoppedDeliberately
                || state.BreakLoop
                || state.ContinueLoop
                || state.SuspendRequested
            )
                return;

            ct.ThrowIfCancellationRequested();

            // Resuming a suspended run: relocate to the exact point it left off before doing any real
            // work again — every sibling before the recorded frame/leaf already ran pre-suspend and
            // must never re-run (S-PIPE-TREE-d3a).
            if (walk.Resume is { } resume)
            {
                if (resume.Index < resume.Path.Count)
                {
                    if (node.Step.Id != resume.Path[resume.Index].BlockStepId)
                        continue;

                    resume.Index++;
                    await ExecuteNodeAsync(node, ctx, state, depth, ct, walk);
                    walk.Resume = null; // consumed — every later sibling here runs normally
                    continue;
                }

                if (node.Step.Id != resume.SuspendedLeafStepId)
                    continue;

                // The suspended leaf itself already executed (it's the one that requested
                // suspension) — never re-run it; resume from the sibling right after it.
                walk.Resume = null;
                continue;
            }

            await ExecuteNodeAsync(node, ctx, state, depth, ct, walk);
        }
    }

    private async Task ExecuteNodeAsync(
        PipelineTreeNode node,
        PipelineExecutionContext ctx,
        PipelineTreeRunState state,
        int depth,
        CancellationToken ct,
        PipelineTreeWalk walk
    )
    {
        PipelineStep step = node.Step;

        // Non-null only when THIS node was just relocated to by a resume (ExecuteNodesAsync matched it
        // and incremented Resume.Index right before calling in) — the frame that says how to resolve
        // this block's arm/case/iteration WITHOUT re-evaluating anything.
        PipelineRunFrame? resumeFrame =
            walk.Resume is { } r && r.Index > 0 && r.Path[r.Index - 1].BlockStepId == step.Id
                ? r.Path[r.Index - 1]
                : null;

        switch (step.BlockKind)
        {
            case null:
                await ExecuteLeafAsync(step, ctx, state, ct);
                break;

            case "if":
            {
                bool matched = resumeFrame is not null
                    ? resumeFrame.Branch == "then"
                    : await EvaluateConditionTreeAsync(ctx, step.Conditions);
                List<PipelineTreeNode> arm =
                [
                    .. node.Children.Where(c => c.Step.Branch == (matched ? "then" : "else")),
                ];
                walk.LivePath.Add(
                    new()
                    {
                        BlockStepId = step.Id,
                        Kind = "if",
                        Branch = matched ? "then" : "else",
                    }
                );
                await ExecuteNodesAsync(arm, ctx, state, depth + 1, ct, walk);
                if (!state.SuspendRequested)
                    walk.LivePath.RemoveAt(walk.LivePath.Count - 1);
                break;
            }

            case "switch":
                await ExecuteSwitchAsync(node, ctx, state, depth, ct, walk, resumeFrame);
                break;

            case "loop":
                await ExecuteLoopAsync(node, ctx, state, depth, ct, walk, resumeFrame);
                break;

            case "random_branch":
                await ExecuteRandomBranchAsync(node, ctx, state, depth, ct, walk, resumeFrame);
                break;

            case "try":
                await ExecuteTryAsync(node, ctx, state, depth, ct, walk, resumeFrame);
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

        if (actionResult.Suspended)
        {
            // Suspension is not a failure — the run parks here cleanly and resumes later
            // (S-PIPE-TREE-d3a). Never combined with break/continue/stop handling below.
            state.SuspendRequested = true;
            state.SuspendStepId = step.Id;
            state.SuspendWaitEventName = actionResult.WaitEventName;
            state.SuspendWaitTimeoutSeconds = actionResult.WaitTimeoutSeconds;
            return;
        }

        if (ctx.ShouldBreakLoop)
        {
            ctx.ShouldBreakLoop = false;
            // Only a "break" with an enclosing loop to act on becomes control flow; outside a loop
            // it is an honest no-op (pipeline-control-flow.md D3) — the step is already logged above.
            if (ctx.LoopDepth > 0)
                state.BreakLoop = true;
        }

        if (ctx.ShouldContinueLoop)
        {
            ctx.ShouldContinueLoop = false;
            if (ctx.LoopDepth > 0)
                state.ContinueLoop = true;
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
        CancellationToken ct,
        PipelineTreeWalk walk,
        PipelineRunFrame? resumeFrame
    )
    {
        PipelineTreeNode? matched;

        if (resumeFrame is not null)
        {
            // Resuming: re-enter the SAME arm that was chosen before suspension, never re-evaluate
            // the switch value — it could have drifted from the restored variable bag in principle,
            // and the whole point of the cursor is to relocate exactly, not re-decide.
            matched = node.Children.FirstOrDefault(c => c.Step.Id == resumeFrame.CaseStepId);
        }
        else
        {
            SwitchBlockConfig? config = ParseBlockConfig<SwitchBlockConfig>(
                node.Step.BlockConfigJson
            );
            string switchValue = ResolveScalar(config?.Value, ctx);

            List<PipelineTreeNode> cases =
            [
                .. node
                    .Children.Where(c => c.Step.BlockKind == "switch_case")
                    .OrderBy(c => c.Step.Order),
            ];

            PipelineTreeNode? defaultCase = null;
            matched = null;
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
        }

        if (matched is null)
            return;

        walk.LivePath.Add(
            new()
            {
                BlockStepId = node.Step.Id,
                Kind = "switch",
                CaseStepId = matched.Step.Id,
            }
        );
        await ExecuteNodesAsync(matched.Children, ctx, state, depth + 1, ct, walk);
        if (!state.SuspendRequested)
            walk.LivePath.RemoveAt(walk.LivePath.Count - 1);
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
        CancellationToken ct,
        PipelineTreeWalk walk,
        PipelineRunFrame? resumeFrame
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

        // Resuming: jump straight to the recorded iteration — every earlier pass already ran and
        // committed its effects before suspension (S-PIPE-TREE-d3a). Fresh runs start at 0 as before.
        int index = resumeFrame?.LoopIndex ?? 0;
        string previousItem =
            resumeFrame is not null && index > 0
                ? mode == "foreach"
                    ? items[index - 1]
                    : (index - 1).ToString()
                : string.Empty;

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

            walk.LivePath.Add(
                new()
                {
                    BlockStepId = node.Step.Id,
                    Kind = "loop",
                    LoopIndex = index,
                }
            );
            PipelineTreeRunState iterationState = new();
            ctx.LoopDepth++;
            try
            {
                await ExecuteNodesAsync(node.Children, ctx, iterationState, depth + 1, ct, walk);
            }
            finally
            {
                ctx.LoopDepth--;
            }

            state.Executed += iterationState.Executed;
            state.Skipped += iterationState.Skipped;

            if (iterationState.SuspendRequested)
            {
                // Leave this iteration's frame on the path — it IS the resume point.
                state.SuspendRequested = true;
                state.SuspendStepId = iterationState.SuspendStepId;
                state.SuspendWaitEventName = iterationState.SuspendWaitEventName;
                state.SuspendWaitTimeoutSeconds = iterationState.SuspendWaitTimeoutSeconds;
                return;
            }

            walk.LivePath.RemoveAt(walk.LivePath.Count - 1);

            if (iterationState.AbortedBudget)
            {
                state.AbortedBudget = true;
                state.AbortReason = iterationState.AbortReason;
                return;
            }
            if (iterationState.BreakLoop)
            {
                // Consumed by THIS loop — the innermost enclosing one. An outer loop, if any,
                // keeps iterating; it never sees this flag (pipeline-control-flow.md D3).
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

            // ContinueLoop is consumed implicitly: it already stopped the rest of this iteration's
            // body (ExecuteNodesAsync's guard), so falling through here just advances to the next pass.
            previousItem = currentItem;
            index++;
        }
    }

    private async Task ExecuteRandomBranchAsync(
        PipelineTreeNode node,
        PipelineExecutionContext ctx,
        PipelineTreeRunState state,
        int depth,
        CancellationToken ct,
        PipelineTreeWalk walk,
        PipelineRunFrame? resumeFrame
    )
    {
        PipelineTreeNode? chosen;

        if (resumeFrame is not null)
        {
            // Resuming: re-enter the SAME case the roll picked before suspension — a fresh roll here
            // would defeat the entire point of persisting the cursor.
            chosen = node.Children.FirstOrDefault(c => c.Step.Id == resumeFrame.CaseStepId);
        }
        else
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
                    (
                        c,
                        ParseBlockConfig<RandomCaseBlockConfig>(c.Step.BlockConfigJson)?.Weight
                            ?? 1m
                    )
                ),
            ];
            decimal total = weighted.Sum(w => w.Weight);
            if (total <= 0)
                return;

            double roll = _randomSource() * (double)total;
            decimal cumulative = 0;
            chosen = null;
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
        }

        if (chosen is null)
            return;

        walk.LivePath.Add(
            new()
            {
                BlockStepId = node.Step.Id,
                Kind = "random_branch",
                CaseStepId = chosen.Step.Id,
            }
        );
        await ExecuteNodesAsync(chosen.Children, ctx, state, depth + 1, ct, walk);
        if (!state.SuspendRequested)
            walk.LivePath.RemoveAt(walk.LivePath.Count - 1);
    }

    private async Task ExecuteTryAsync(
        PipelineTreeNode node,
        PipelineExecutionContext ctx,
        PipelineTreeRunState state,
        int depth,
        CancellationToken ct,
        PipelineTreeWalk walk,
        PipelineRunFrame? resumeFrame
    )
    {
        List<PipelineTreeNode> body = [.. node.Children.Where(c => c.Step.Branch == "then")];
        List<PipelineTreeNode> catchChildren =
        [
            .. node.Children.Where(c => c.Step.Branch == "else"),
        ];

        // Resuming directly into the catch arm: the body already ran to completion (and failed) before
        // suspension, so re-running it would duplicate its effects — go straight to catch instead.
        if (resumeFrame is { Branch: "else" })
        {
            walk.LivePath.Add(
                new()
                {
                    BlockStepId = node.Step.Id,
                    Kind = "try",
                    Branch = "else",
                }
            );
            PipelineTreeRunState resumedCatchState = new();
            await ExecuteNodesAsync(catchChildren, ctx, resumedCatchState, depth + 1, ct, walk);
            FoldTryCatchResult(state, walk, node.Step.Id, resumedCatchState);
            return;
        }

        walk.LivePath.Add(
            new()
            {
                BlockStepId = node.Step.Id,
                Kind = "try",
                Branch = "then",
            }
        );
        PipelineTreeRunState bodyState = new();
        await ExecuteNodesAsync(body, ctx, bodyState, depth + 1, ct, walk);

        state.Executed += bodyState.Executed;
        state.Skipped += bodyState.Skipped;

        if (bodyState.SuspendRequested)
        {
            // Never caught by this try's catch — same posture as break/continue below. Leaves the
            // "then" frame on the path as the resume point.
            state.SuspendRequested = true;
            state.SuspendStepId = bodyState.SuspendStepId;
            state.SuspendWaitEventName = bodyState.SuspendWaitEventName;
            state.SuspendWaitTimeoutSeconds = bodyState.SuspendWaitTimeoutSeconds;
            return;
        }

        walk.LivePath.RemoveAt(walk.LivePath.Count - 1);

        if (bodyState.AbortedBudget)
        {
            // A budget breach is never swallowed — every cap breach aborts the run cleanly
            // regardless of which block it tripped inside (pipeline-control-flow.md D6).
            state.AbortedBudget = true;
            state.AbortReason = bodyState.AbortReason;
            return;
        }

        if (bodyState.BreakLoop || bodyState.ContinueLoop)
        {
            // Deliberate loop control flow is never caught as a failure — it bubbles straight past
            // this try (and its catch arm never runs) to the enclosing loop that will consume it.
            state.BreakLoop = bodyState.BreakLoop;
            state.ContinueLoop = bodyState.ContinueLoop;
            return;
        }

        if (bodyState.FailedBreak)
        {
            walk.LivePath.Add(
                new()
                {
                    BlockStepId = node.Step.Id,
                    Kind = "try",
                    Branch = "else",
                }
            );
            PipelineTreeRunState catchState = new();
            await ExecuteNodesAsync(catchChildren, ctx, catchState, depth + 1, ct, walk);
            FoldTryCatchResult(state, walk, node.Step.Id, catchState);
            return;
        }

        if (bodyState.StoppedDeliberately)
            state.StoppedDeliberately = true;
    }

    /// <summary>Shared fold logic for a <c>try</c> block's catch arm, used both by the normal
    /// body-failed path and by a resume that relocates straight into the catch arm.</summary>
    private static void FoldTryCatchResult(
        PipelineTreeRunState state,
        PipelineTreeWalk walk,
        Guid tryStepId,
        PipelineTreeRunState catchState
    )
    {
        state.Executed += catchState.Executed;
        state.Skipped += catchState.Skipped;

        if (catchState.SuspendRequested)
        {
            state.SuspendRequested = true;
            state.SuspendStepId = catchState.SuspendStepId;
            state.SuspendWaitEventName = catchState.SuspendWaitEventName;
            state.SuspendWaitTimeoutSeconds = catchState.SuspendWaitTimeoutSeconds;
            return;
        }

        walk.LivePath.RemoveAt(walk.LivePath.Count - 1);

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
            PipelineId = request.PipelineId,
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
            // completed (or failed) and its result has already been returned to the caller. The
            // rejected row must also be DETACHED: it would otherwise stay tracked on this shared
            // scoped DbContext and poison every subsequent SaveChangesAsync on the same scope
            // (e.g. ChatMessagePersistenceHandler saving on the same request).
            // Best-effort, and it MUST stay that way: this runs inside the catch that guarantees
            // telemetry never takes down command execution, so anything thrown here would defeat the
            // very guarantee it is protecting.
            try
            {
                _db.Entry(row).State = EntityState.Detached;
            }
            catch (Exception detachEx)
            {
                _logger.LogDebug(
                    detachEx,
                    "Could not detach the rejected PipelineExecution row for channel {BroadcasterId}.",
                    request.BroadcasterId
                );
            }
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
            PipelineOutcome.Suspended => "suspended",
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

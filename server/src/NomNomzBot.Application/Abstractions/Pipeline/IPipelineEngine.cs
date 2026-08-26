// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Abstractions.Pipeline;

public interface IPipelineEngine
{
    Task<PipelineExecutionResult> ExecuteAsync(
        PipelineRequest request,
        CancellationToken ct = default
    );
    Task CancelAllForChannelAsync(Guid broadcasterId);
    int GetActiveCountForChannel(Guid broadcasterId);

    /// <summary>
    /// Invoked by the <c>run_pipeline</c> action in <c>inline</c> mode (pipeline-control-flow.md D4):
    /// runs the target pipeline's tree using the CALLER's own <paramref name="callerCtx"/> — same Run
    /// scope (<see cref="PipelineExecutionContext.Variables"/>), same <see cref="PipelineExecutionContext.CallDepth"/>
    /// counter, so a chain of inline calls across any number of pipelines is bounded by one shared cap.
    /// Fails closed (never runs a single step of the callee) when: the caller is already at the
    /// recursion cap, or the target pipeline does not belong to the caller's own channel (tenant
    /// scoping — <c>platform-conventions.md</c>). On success, returns the callee's <c>return_value</c>
    /// (or <c>null</c> if it never returned one).
    /// </summary>
    Task<Result<string?>> RunInlineSubPipelineAsync(
        PipelineExecutionContext callerCtx,
        Guid targetPipelineId,
        IReadOnlyList<string>? args,
        CancellationToken ct = default
    );

    /// <summary>
    /// Resumes a previously suspended run from its persisted <c>PipelineRunState</c> — a fresh engine
    /// instance over the same row (e.g. after a process restart) continues at the exact next step with
    /// its variable bag, loop cursors and switch/try arm intact (S-PIPE-TREE-d3a). Wall-clock time spent
    /// suspended is excluded from <c>MaxRuntime</c>: only <see cref="PipelineRunState.AccumulatedRuntimeMs"/>
    /// counts toward the cap, so a run parked for an hour has not "run" for an hour. Returns
    /// <see cref="PipelineOutcome.Failed"/> when no suspended row matches <paramref name="runStateId"/>.
    /// </summary>
    Task<PipelineExecutionResult> ResumeAsync(Guid runStateId, CancellationToken ct = default);

    /// <summary>
    /// A named event fired for a channel (S-PIPE-TREE-d3b) — resumes every suspended run for that
    /// channel currently parked on a <c>wait_for_event</c> step whose <c>event_name</c> equals
    /// <paramref name="eventName"/> exactly (case-insensitive). A run waiting for a DIFFERENT name is
    /// left untouched — publishing one event must never wake a waiter parked on another. Each resumed
    /// run gets <paramref name="eventData"/> merged into its variable bag under the <c>event.*</c>
    /// namespace (plus <c>event.name</c> and <c>event.matched=true</c>/<c>event.timed_out=false</c>),
    /// readable by every step after the wait. Returns the number of runs resumed.
    /// </summary>
    Task<int> ResumeSuspendedRunsForEventAsync(
        Guid broadcasterId,
        string eventName,
        IReadOnlyDictionary<string, string> eventData,
        CancellationToken ct = default
    );

    /// <summary>
    /// Resumes every suspended run across every channel whose <c>wait_for_event</c> timeout has
    /// elapsed (S-PIPE-TREE-d3b) — an honest timeout path: the run is NOT failed and NOT left parked
    /// forever, it continues past the wait with <c>event.timed_out=true</c>/<c>event.matched=false</c>
    /// so the pipeline author can branch on it (e.g. an <c>if</c> right after the wait). Meant to be
    /// polled by a scheduler; exposed here so the policy itself is independently testable. Returns the
    /// number of runs resumed.
    /// </summary>
    Task<int> ResumeTimedOutWaitsAsync(CancellationToken ct = default);
}

public class PipelineRequest
{
    /// <summary>The tenant (channel) Guid this pipeline runs for (schema §1.1, internal key).</summary>
    public required Guid BroadcasterId { get; init; }

    /// <summary>
    /// When set, the engine loads PipelineStep rows from the database (preferred path).
    /// Falls back to <see cref="PipelineJson"/> if no steps are found.
    /// </summary>
    public Guid? PipelineId { get; init; }

    /// <summary>
    /// Legacy / fallback graph JSON. Used when PipelineId is null or has no DB steps.
    /// </summary>
    public string PipelineJson { get; init; } = "{}";

    public required string TriggeredByUserId { get; init; }
    public required string TriggeredByDisplayName { get; init; }
    public string? MessageId { get; init; }
    public string? RedemptionId { get; init; }
    public string? RewardId { get; init; }
    public string RawMessage { get; init; } = "";
    public Dictionary<string, string> InitialVariables { get; init; } = new();
}

public class PipelineExecutionResult
{
    public required string ExecutionId { get; init; }
    public required PipelineOutcome Outcome { get; init; }
    public required TimeSpan Duration { get; init; }
    public int StepsExecuted { get; init; }
    public int StepsSkipped { get; init; }
    public int Total { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<StepExecutionLog> StepLogs { get; init; } = [];

    /// <summary>Set only when <see cref="Outcome"/> is <see cref="PipelineOutcome.Suspended"/> — the
    /// leaf step the run is now parked at.</summary>
    public Guid? SuspendedAtStepId { get; init; }

    /// <summary>Set only when <see cref="Outcome"/> is <see cref="PipelineOutcome.Suspended"/> — the
    /// block-nesting cursor path (JSON), persisted verbatim into <c>PipelineRunState.CursorJson</c> so a
    /// later resume can walk back down to <see cref="SuspendedAtStepId"/> (S-PIPE-TREE-d3a).</summary>
    public string? SuspendCursorJson { get; init; }

    /// <summary>Set only when <see cref="Outcome"/> is <see cref="PipelineOutcome.Suspended"/> — the
    /// persisted <c>PipelineRunState.Id</c> to pass to <see cref="IPipelineEngine.ResumeAsync"/> later.</summary>
    public Guid? SuspendedRunStateId { get; init; }

    /// <summary>Set only when the suspending leaf was <c>wait_for_event</c> (S-PIPE-TREE-d3b) — the
    /// named event the run is now parked waiting for, persisted onto <c>PipelineRunState.WaitEventName</c>.</summary>
    public string? SuspendWaitEventName { get; init; }

    /// <summary>Set only alongside <see cref="SuspendWaitEventName"/> — seconds from now the wait is
    /// allowed to stay parked, persisted as an absolute <c>PipelineRunState.WaitTimeoutAt</c>.</summary>
    public int? SuspendWaitTimeoutSeconds { get; init; }
}

public enum PipelineOutcome
{
    Completed,
    Stopped,
    Failed,

    /// <summary>
    /// A step broke the run early because an action FAILED (fail-closed, no <c>continue_on_error</c>) —
    /// distinct from <see cref="Completed"/> (every step ran or was skipped by a condition) and
    /// <see cref="Stopped"/> (a deliberate <c>stop</c> action or a matched <c>stop_on_match</c> step).
    /// A run that never reached its last step because something broke must never report success.
    /// </summary>
    PartiallyFailed,
    TimedOut,
    Cancelled,

    /// <summary>
    /// A tree-execution safety cap tripped (recursion depth, loop iteration count, or loop runtime
    /// guard) — the run was aborted cleanly rather than being allowed to wedge the bot mid-stream
    /// (pipeline-control-flow.md D6, pipeline-tree-and-editor.md §2.4/§2.6). The tripped cap's
    /// reason is recorded on <see cref="PipelineExecutionResult.ErrorMessage"/>.
    /// </summary>
    AbortedBudget,

    /// <summary>A leaf action requested suspension (S-PIPE-TREE-d3a) — the run's state is persisted to
    /// <c>PipelineRunState</c> and it will resume later at the exact next step, not lost to an
    /// in-memory-only continuation.</summary>
    Suspended,
}

public class StepExecutionLog
{
    public required int StepIndex { get; init; }
    public required string ActionType { get; init; }
    public required bool Succeeded { get; init; }
    public required TimeSpan Duration { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Output { get; init; }
}

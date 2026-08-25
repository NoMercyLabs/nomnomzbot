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

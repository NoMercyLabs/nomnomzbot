// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json.Serialization;
using NomNomzBot.Domain.Commands.Entities;

namespace NomNomzBot.Infrastructure.Platform.Pipeline;

/// <summary>
/// In-memory tree node for a tree-shaped pipeline run (pipeline-tree-and-editor.md §2.1). Built once
/// per execution from the flat <see cref="PipelineStep"/> rows via <c>ParentStepId</c>/<c>Order</c>;
/// never persisted.
/// </summary>
internal sealed class PipelineTreeNode
{
    public required PipelineStep Step { get; init; }
    public List<PipelineTreeNode> Children { get; init; } = [];
}

/// <summary>
/// Accumulates the outcome of walking one list of sibling nodes (a block's body/arm, or the top-level
/// run). A budget breach or a fail-closed break short-circuits every enclosing walk it bubbles through
/// (checked by the caller after each recursive call) rather than throwing — control flow, not an error.
/// </summary>
internal sealed class PipelineTreeRunState
{
    public int Executed;
    public int Skipped;
    public bool FailedBreak;
    public bool StoppedDeliberately;
    public bool AbortedBudget;
    public string? AbortReason;

    /// <summary>A <c>break</c> action fired somewhere in this walk; bubbles up unconsumed until the
    /// innermost enclosing <c>loop</c> block claims it (pipeline-control-flow.md D3). Distinct from
    /// <see cref="FailedBreak"/> — this is deliberate control flow, never a caught failure.</summary>
    public bool BreakLoop;

    /// <summary>A <c>continue</c> action fired somewhere in this walk; bubbles up unconsumed until the
    /// innermost enclosing <c>loop</c> block claims it.</summary>
    public bool ContinueLoop;

    /// <summary>A leaf action returned <see cref="ActionResult.Suspended"/>; bubbles all the way up to
    /// <c>RunTreeAsync</c> — never caught by an enclosing <c>try</c> (same posture as break/continue),
    /// since a suspended run has not failed. S-PIPE-TREE-d3a.</summary>
    public bool SuspendRequested;

    /// <summary>The leaf step that requested suspension, once <see cref="SuspendRequested"/> is set.</summary>
    public Guid? SuspendStepId;

    /// <summary>Set alongside <see cref="SuspendRequested"/> when the suspending leaf was
    /// <c>wait_for_event</c> (S-PIPE-TREE-d3b) — the named event it is now parked waiting for.</summary>
    public string? SuspendWaitEventName;

    /// <summary>Set alongside <see cref="SuspendWaitEventName"/> — seconds from now the wait may stay
    /// parked before the engine resumes it down the timeout path.</summary>
    public int? SuspendWaitTimeoutSeconds;
}

/// <summary>
/// One entry in the block-nesting path from the run's roots down to the block directly containing the
/// step that suspended the run (S-PIPE-TREE-d3a). Captured live as the tree walk descends (pushed before
/// recursing into a child list, popped again on the way back out — UNLESS the walk is unwinding because
/// of a suspend, in which case the frame is left in place) and persisted as <c>PipelineRunState.CursorJson</c>
/// so a later resume can walk the exact same path back down to the exact same point.
/// </summary>
internal sealed class PipelineRunFrame
{
    /// <summary>The block step (if/switch/loop/random_branch/try) this frame descends through.</summary>
    public required Guid BlockStepId { get; init; }

    /// <summary>if | switch | loop | random_branch | try.</summary>
    public required string Kind { get; init; }

    /// <summary>if: "then"/"else". try: "then" (body) / "else" (catch).</summary>
    public string? Branch { get; init; }

    /// <summary>switch: the matched switch_case step id. random_branch: the chosen random_case step id.</summary>
    public Guid? CaseStepId { get; init; }

    /// <summary>loop: the 0-based iteration index the walk was inside when it suspended.</summary>
    public int? LoopIndex { get; init; }
}

/// <summary>
/// Drives a resumed tree walk back down to the exact point a prior run suspended at, then hands control
/// back to normal execution. Consumed top-down: each level matches the next unconsumed <see cref="PipelineRunFrame"/>
/// against its own children, skips every preceding sibling (already executed before suspension), recurses
/// into the matching one, then goes null for every sibling that follows — from there the walk is indistinguishable
/// from a fresh run. S-PIPE-TREE-d3a.
/// </summary>
internal sealed class PipelineResumeCursor
{
    public required IReadOnlyList<PipelineRunFrame> Path { get; init; }
    public int Index { get; set; }
    public required Guid SuspendedLeafStepId { get; init; }
}

/// <summary>Threaded through the whole tree walk: <see cref="Resume"/> drives a resumed run back down
/// to its suspend point (consumed and nulled out once reached — see <see cref="PipelineResumeCursor"/>);
/// <see cref="LivePath"/> is pushed/popped by each block handler as it descends/backtracks, so whatever
/// is still on it at the moment <c>PipelineTreeRunState.SuspendRequested</c> fires IS the new cursor path
/// to persist. S-PIPE-TREE-d3a.</summary>
internal sealed class PipelineTreeWalk
{
    public PipelineResumeCursor? Resume { get; set; }
    public List<PipelineRunFrame> LivePath { get; } = [];
}

// ─── Block-kind configuration DTOs (PipelineStep.BlockConfigJson) ────────────

internal sealed class SwitchBlockConfig
{
    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

internal sealed class SwitchCaseBlockConfig
{
    [JsonPropertyName("match")]
    public string? Match { get; set; }

    [JsonPropertyName("operator")]
    public string? Operator { get; set; }

    [JsonPropertyName("is_default")]
    public bool IsDefault { get; set; }
}

internal sealed class LoopBlockConfig
{
    /// <summary>repeat | foreach | while.</summary>
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    /// <summary>Iteration count for <c>repeat</c> mode.</summary>
    [JsonPropertyName("count")]
    public int? Count { get; set; }

    /// <summary>Run-scope variable name (or literal) holding a CSV list, for <c>foreach</c> mode.</summary>
    [JsonPropertyName("list_var")]
    public string? ListVar { get; set; }

    /// <summary>Per-loop iteration cap; clamped to the engine's hard ceiling, never raises it.</summary>
    [JsonPropertyName("max_iterations")]
    public int? MaxIterations { get; set; }

    /// <summary>Optional tighter runtime guard than the whole-run budget (pipeline-tree-and-editor.md §2.4).</summary>
    [JsonPropertyName("max_loop_runtime_seconds")]
    public int? MaxLoopRuntimeSeconds { get; set; }
}

internal sealed class RandomCaseBlockConfig
{
    [JsonPropertyName("weight")]
    public decimal? Weight { get; set; }
}

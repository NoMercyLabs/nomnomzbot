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

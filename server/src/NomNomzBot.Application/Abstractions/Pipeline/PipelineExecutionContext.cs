// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.Pipeline;

// ─── Execution context ────────────────────────────────────────────────────────

/// <summary>
/// Mutable per-execution context. Never shared between executions.
/// </summary>
public sealed class PipelineExecutionContext
{
    public string ExecutionId { get; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>The tenant (channel) Guid this execution belongs to (schema §1.1, internal key).</summary>
    public required Guid BroadcasterId { get; init; }
    public required string TriggeredByUserId { get; init; }
    public required string TriggeredByDisplayName { get; init; }
    public required string MessageId { get; init; }
    public string? RedemptionId { get; init; }
    public string? RewardId { get; init; }
    public required string RawMessage { get; init; }
    public CancellationToken CancellationToken { get; init; }

    /// <summary>Pipeline-scoped variables. Keys without braces.</summary>
    public Dictionary<string, string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int CurrentStepIndex { get; set; }
    public bool ShouldStop { get; set; }

    /// <summary>Set by the <c>break</c> action; consumed by the innermost enclosing <c>loop</c> block.
    /// Outside a loop (<see cref="LoopDepth"/> == 0) it is an honest no-op — the engine never sets it
    /// (pipeline-control-flow.md D3).</summary>
    public bool ShouldBreakLoop { get; set; }

    /// <summary>Set by the <c>continue</c> action; consumed by the innermost enclosing <c>loop</c> block.
    /// Outside a loop it is an honest no-op, same as <see cref="ShouldBreakLoop"/>.</summary>
    public bool ShouldContinueLoop { get; set; }

    /// <summary>Nesting depth of <c>loop</c> blocks currently being walked; 0 means "not inside a loop".
    /// Used to decide whether a <c>break</c>/<c>continue</c> action has anywhere to act on.</summary>
    public int LoopDepth { get; set; }

    /// <summary>Per-step execution logs accumulated during the run.</summary>
    public List<StepExecutionLog> StepLogs { get; } = [];
}

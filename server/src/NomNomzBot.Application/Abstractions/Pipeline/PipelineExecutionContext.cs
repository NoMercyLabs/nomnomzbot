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

    /// <summary>The activity-feed row id (a <c>DomainEventBase.EventId</c>) of the channel event that
    /// triggered this run, when the trigger was one — e.g. a reward redemption. Threaded down to
    /// <c>play_tts</c> so its dispatched utterance can correlate a Replay capture back to that event.
    /// <c>null</c> for chat-command/timer triggers, which log no ChannelEvent.</summary>
    public string? ChannelEventId { get; init; }

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

    /// <summary>Number of <c>run_pipeline inline</c> frames currently open on THIS execution — spans
    /// pipeline boundaries (A calls B calls A increments the same counter), because an inline call
    /// shares this very context rather than creating a fresh one. Checked against
    /// <c>PipelineEngine.MaxRecursionDepth</c> before each new inline call; a chain that would exceed
    /// it is rejected before the callee's tree is even loaded (pipeline-control-flow.md D4).</summary>
    public int CallDepth { get; set; }

    /// <summary>Set by the <c>return_value</c> action; read by the caller's <c>run_pipeline inline</c>
    /// step immediately after the callee's tree walk finishes, then handed to the caller as
    /// <c>{{call.result}}</c> (pipeline-tree-and-editor.md §2.5). Cleared before each inline call so a
    /// callee that never returns leaves the caller with <c>null</c>, not a stale value from an earlier
    /// sibling call.</summary>
    public string? ReturnValue { get; set; }

    /// <summary>Per-step execution logs accumulated during the run.</summary>
    public List<StepExecutionLog> StepLogs { get; } = [];
}

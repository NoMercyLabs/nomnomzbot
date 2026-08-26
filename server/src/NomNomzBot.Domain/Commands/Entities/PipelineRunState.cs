// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Commands.Entities;

/// <summary>
/// Persists a tree-shaped pipeline run that has been suspended mid-execution, so it can resume after a
/// process restart at the exact next step with its variable bag, loop cursors and call stack intact —
/// an in-memory-only continuation is worthless, exactly like the song-request in-flight marker bug it
/// deliberately avoids repeating. A run that never suspends never gets a row here (zero behavior change
/// for today's fire-and-forget runs). Schema: pipeline-tree-and-editor.md §1.4 (persistence core only —
/// the event-matching/timeout policy half is S-PIPE-TREE-d3b, not modeled here).
/// </summary>
public class PipelineRunState : BaseEntity, ITenantScoped
{
    /// <summary>Equal to the owning <c>PipelineExecutionContext.ExecutionId</c>.</summary>
    public Guid Id { get; set; }

    public Guid BroadcasterId { get; set; }

    public Guid PipelineId { get; set; }

    /// <summary>running | suspended | completed | failed | cancelled.</summary>
    [MaxLength(20)]
    public string Status { get; set; } = null!;

    /// <summary>The leaf step the run is currently suspended at.</summary>
    public Guid? SuspendedAtStepId { get; set; }

    /// <summary>Full variable bag at the point of suspension (JSON object, string→string).</summary>
    public string VariablesJson { get; set; } = "{}";

    /// <summary>Block-nesting path from the run's roots down to the block directly containing
    /// <see cref="SuspendedAtStepId"/> — the loop iteration index and switch/try/random arm chosen at
    /// each level (JSON array of frames). Empty array for a top-level suspend.</summary>
    public string CursorJson { get; set; } = "[]";

    public Guid TriggeredByUserId { get; set; }

    [MaxLength(255)]
    public string TriggeredByDisplayName { get; set; } = null!;

    /// <summary>Wall-clock runtime already consumed by this run BEFORE the current suspension, in
    /// milliseconds — excludes every suspended interval, so <c>MaxRuntime</c> only ever counts time the
    /// engine was actually doing work (settled CTO decision: a run paused for an hour has not "run" for
    /// an hour).</summary>
    public int AccumulatedRuntimeMs { get; set; }

    /// <summary>The named event this run is parked waiting for (S-PIPE-TREE-d3b's <c>wait_for_event</c>
    /// action) — null unless the leaf it suspended at was a wait. Matched case-insensitively against a
    /// published event's name; a non-matching event never resumes this row.</summary>
    [MaxLength(200)]
    public string? WaitEventName { get; set; }

    /// <summary>Absolute deadline for <see cref="WaitEventName"/> — once elapsed, the run is resumed
    /// down the honest timeout path (never left parked forever, never silently dropped) rather than
    /// waiting for a match that may never come. Null unless waiting on an event.</summary>
    public DateTimeOffset? WaitTimeoutAt { get; set; }

    public DateTimeOffset? SuspendedAt { get; set; }

    public DateTimeOffset? ResumedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    [ForeignKey(nameof(PipelineId))]
    public virtual Pipeline Pipeline { get; set; } = null!;
}

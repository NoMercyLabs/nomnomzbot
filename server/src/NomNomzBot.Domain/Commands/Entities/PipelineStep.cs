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
/// One step in a <see cref="Pipeline"/>: an action block with optional branch nesting.
/// Unknown <see cref="ActionType"/> values are rejected at save time (fail-closed).
/// Schema: H.2 (commands-pipelines.md §1).
/// </summary>
public class PipelineStep : BaseEntity, ITenantScoped
{
    public Guid Id { get; set; }
    public Guid PipelineId { get; set; }
    public Guid BroadcasterId { get; set; }

    /// <summary>Parent step id for branch nesting; null for top-level steps.</summary>
    public Guid? ParentStepId { get; set; }

    /// <summary>Branch lane when nested: then | else | null.</summary>
    [MaxLength(10)]
    public string? Branch { get; set; }

    /// <summary>
    /// Block kind (pipeline-control-flow.md D1/D7, pipeline-tree-and-editor.md §1.1). Null = a leaf
    /// action step (today's behavior, unchanged). Non-null values:
    /// if | switch | switch_case | loop | random_branch | random_case | try | detached_step.
    /// A non-null value makes this step a block that owns ordered child steps via
    /// <see cref="ParentStepId"/>/<see cref="Branch"/>.
    /// </summary>
    [MaxLength(20)]
    public string? BlockKind { get; set; }

    /// <summary>
    /// Block-kind-specific configuration (if/while condition pointer, switch value + case-match,
    /// loop mode/list-var/count, random_case weight, ...). Null/"{}" for a leaf action step or a
    /// block kind with no parameters (try, detached_step). Never null for a persisted block step.
    /// </summary>
    public string? BlockConfigJson { get; set; }

    /// <summary>Execution order within the pipeline (unique per pipeline).</summary>
    public int Order { get; set; }

    /// <summary>
    /// When <c>true</c>, this step's failure does not abort the run — the next step still runs
    /// (matches <c>PipelineStepDefinition.ContinueOnError</c>, wired through by
    /// <c>PipelineGraphBuilder</c> for the flat-graph runtime real chat commands actually execute
    /// through; <c>detached_step</c>/<c>try</c> block-kinds only run via direct PipelineId invocation
    /// — the flat graph reader has no concept of nested blocks at all). Defaults to <c>false</c>
    /// (fail-closed).
    /// </summary>
    public bool ContinueOnError { get; set; }

    /// <summary>
    /// Snake_case registry key for the action type (e.g. "send_message", "timeout_user").
    /// Unknown values are rejected at save time; the engine hard-fails on unknown types.
    /// </summary>
    [MaxLength(60)]
    public string ActionType { get; set; } = null!;

    /// <summary>
    /// Action configuration. Must NOT contain tenant credentials, OAuth tokens, or raw URLs —
    /// enforced by <c>ICommandConfigValidator</c> at save time.
    /// </summary>
    public string ConfigJson { get; set; } = "{}";

    /// <summary>EF Core schema version; used as per-row upcast anchor.</summary>
    public int ConfigSchemaVersion { get; set; } = 1;

    /// <summary>FK to a CodeScript; only populated when <see cref="ActionType"/> is "run_code".</summary>
    public Guid? CodeScriptId { get; set; }

    public bool IsEnabled { get; set; } = true;

    public virtual ICollection<PipelineStepCondition> Conditions { get; set; } = [];

    [ForeignKey(nameof(PipelineId))]
    public virtual Pipeline Pipeline { get; set; } = null!;
}

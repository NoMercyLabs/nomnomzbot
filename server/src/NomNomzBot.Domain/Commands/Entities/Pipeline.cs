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
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Commands.Entities;

/// <summary>
/// A named pipeline created via the visual block builder in the frontend.
/// The normalized <see cref="Steps"/> collection is the execution truth;
/// <see cref="GraphJsonCache"/> is a build-time cache regenerated from the steps.
/// Schema: H.1 (commands-pipelines.md §1).
/// </summary>
public class Pipeline : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; }
    public Guid BroadcasterId { get; set; }

    [MaxLength(200)]
    public string Name { get; set; } = null!;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>What triggered this pipeline: command | event | timer | manual | webhook.</summary>
    [MaxLength(30)]
    public string TriggerKind { get; set; } = "manual";

    public bool IsEnabled { get; set; } = true;

    /// <summary>Maximum number of steps permitted at save time (save-fail guard).</summary>
    public int MaxStepCount { get; set; } = 50;

    public long TriggerCount { get; set; }

    public DateTime? LastTriggeredAt { get; set; }

    /// <summary>
    /// Cached JSON representation regenerated from <see cref="Steps"/>. The engine reads the
    /// normalized step rows and uses this only as a performance cache; it must never be the
    /// sole truth.
    /// </summary>
    public string? GraphJsonCache { get; set; }

    /// <summary>
    /// Declared named parameters for this pipeline when invoked as a sub-pipeline via
    /// <c>run_pipeline</c> (S-PIPE-TREE-d2b) — a JSON string array, e.g. <c>["user", "amount"]</c>.
    /// The editor renders one labelled field per declared name instead of an unlabelled positional
    /// list; a caller binds arguments BY NAME (<c>RunPipelineAction</c>'s <c>named_args</c> field),
    /// never by position. Null/empty = no declared parameters — a caller may still pass legacy
    /// positional args, read by the callee as <c>{{args.1}}</c>..<c>{{args.N}}</c>.
    /// </summary>
    public string? ParameterNamesJson { get; set; }

    public virtual ICollection<PipelineStep> Steps { get; set; } = [];

    /// <summary>N independent trigger bindings (E1) — the source of truth once populated; see
    /// <see cref="PipelineTrigger"/>. Empty until the pipeline is first saved by the new tree
    /// editor (pipeline-tree-and-editor.md §6.1) — <see cref="TriggerKind"/> stays authoritative
    /// for the dispatcher until then.</summary>
    public virtual ICollection<PipelineTrigger> Triggers { get; set; } = [];

    /// <summary>Which <c>PlatformContentDefinition</c> (Kind=<c>pipeline</c>) this row was installed/seeded
    /// from; null for a pipeline never installed via the platform-content spine (platform-admin.md §3.3).
    /// Generalizes the same provenance shape already applied to <c>ChannelBuiltinCommand</c> and
    /// <c>Widget</c>.</summary>
    public Guid? PlatformSourceDefinitionId { get; set; }

    /// <summary>The <c>PlatformContentVersion.Version</c> installed.</summary>
    public int? PlatformSourceVersion { get; set; }

    /// <summary>The <c>ContentHash</c> at install/last-sync time — compared against the canonicalized hash
    /// of this row's live <see cref="GraphJsonCache"/> to decide "untouched" for
    /// <c>update_in_place_where_untouched</c> publishes.</summary>
    [MaxLength(64)]
    public string? PlatformSourceHash { get; set; }

    /// <summary>When this row last received a platform-originated update.</summary>
    public DateTime? PlatformSourceSyncedAt { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}

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

    public virtual ICollection<PipelineStep> Steps { get; set; } = [];

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}

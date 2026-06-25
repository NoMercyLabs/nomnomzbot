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

public class Command : SoftDeletableEntity, ITenantScoped
{
    public int Id { get; set; }
    public Guid BroadcasterId { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = null!;

    [MaxLength(20)]
    public string Permission { get; set; } = "everyone";

    [MaxLength(20)]
    public string Type { get; set; } = "text";

    [MaxLength(2000)]
    public string? Response { get; set; }

    public List<string> Responses { get; set; } = [];

    /// <summary>
    /// Inline pipeline JSON — used when the command carries its own pipeline steps without a
    /// separate named Pipeline entity. Prefer <see cref="PipelineId"/> when binding to a shared
    /// Pipeline; this field is the fallback for legacy / one-off steps.
    /// </summary>
    public string? PipelineJson { get; set; }

    /// <summary>FK to a named <see cref="Pipeline"/> entity. When set, the channel registry
    /// loads the pipeline's <see cref="Pipeline.GraphJson"/> into <c>CachedCommand.PipelineJson</c>
    /// so the engine always works from a single resolved JSON blob.</summary>
    public int? PipelineId { get; set; }

    [ForeignKey(nameof(PipelineId))]
    public virtual Pipeline? Pipeline { get; set; }

    public bool IsEnabled { get; set; } = true;

    [MaxLength(500)]
    public string? Description { get; set; }

    public int CooldownSeconds { get; set; }

    public bool CooldownPerUser { get; set; }

    public List<string> Aliases { get; set; } = [];

    public bool IsPlatform { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}

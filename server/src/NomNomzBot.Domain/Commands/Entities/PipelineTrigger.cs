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
/// One independent trigger binding for a <see cref="Pipeline"/>. A pipeline has 1..N trigger
/// rows, each independently a command | event | timer | manual | webhook binding — replaces the
/// single <see cref="Pipeline.TriggerKind"/> enum column as the source of truth (that column is
/// kept as a denormalized summary for list-view display only). Schema: pipeline-tree-and-editor.md
/// §1.3 (E1).
/// </summary>
public class PipelineTrigger : BaseEntity, ITenantScoped
{
    public Guid Id { get; set; }
    public Guid PipelineId { get; set; }
    public Guid BroadcasterId { get; set; }

    /// <summary>Trigger kind: command | event | timer | manual | webhook.</summary>
    [MaxLength(20)]
    public string Kind { get; set; } = null!;

    /// <summary>Display order among a pipeline's triggers; unique per pipeline.</summary>
    public int Order { get; set; }

    /// <summary>
    /// Kind-specific configuration: command → { Name, Aliases, PrefixMode, CustomPrefix, MatchMode,
    /// MatchPattern }; event → { EventType }; timer → { TimerId }; webhook → { EndpointId };
    /// manual → {}.
    /// </summary>
    public string ConfigJson { get; set; } = "{}";

    public bool IsEnabled { get; set; } = true;

    [ForeignKey(nameof(PipelineId))]
    public virtual Pipeline Pipeline { get; set; } = null!;
}

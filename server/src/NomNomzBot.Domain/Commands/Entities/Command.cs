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
/// An authored command: T1 template, T2 visual pipeline, or T3 code-triggered.
/// Schema: G.2 (commands-pipelines.md §1).
/// </summary>
public class Command : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; }
    public Guid BroadcasterId { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = null!;

    /// <summary>Lowercased, trimmed name used for uniqueness and lookup matching.</summary>
    [MaxLength(100)]
    public string NameNormalized { get; set; } = null!;

    /// <summary>How the prefix is resolved: Default (channel prefix), Custom, or None.</summary>
    [MaxLength(20)]
    public string PrefixMode { get; set; } = "Default";

    /// <summary>Custom prefix character(s) when <see cref="PrefixMode"/> is Custom.</summary>
    [MaxLength(8)]
    public string? CustomPrefix { get; set; }

    /// <summary>How the trigger input is matched against <see cref="Name"/>.</summary>
    [MaxLength(20)]
    public string MatchMode { get; set; } = "StartsWith";

    /// <summary>Required when <see cref="MatchMode"/> is Regex; null otherwise.</summary>
    [MaxLength(200)]
    public string? MatchPattern { get; set; }

    /// <summary>Authoring tier: template | pipeline | code.</summary>
    [MaxLength(20)]
    public string Tier { get; set; } = "template";

    [MaxLength(500)]
    public string? Description { get; set; }

    public List<string> Aliases { get; set; } = [];

    /// <summary>Static text response for T1 template commands.</summary>
    [MaxLength(2000)]
    public string? TemplateResponse { get; set; }

    /// <summary>Multiple response variations for T1 template commands (random selection).</summary>
    public List<string>? TemplateResponses { get; set; }

    /// <summary>EF Core schema version; used as per-row upcast anchor.</summary>
    public int ConfigSchemaVersion { get; set; } = 1;

    /// <summary>FK to a named <see cref="Pipeline"/> entity that this command executes.</summary>
    public Guid? PipelineId { get; set; }

    [ForeignKey(nameof(PipelineId))]
    public virtual Pipeline? Pipeline { get; set; }

    /// <summary>Minimum community standing level required to invoke this command.</summary>
    public int MinPermissionLevel { get; set; }

    public int CooldownSeconds { get; set; }

    public int UserCooldownSeconds { get; set; }

    public bool CooldownPerUser { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>True for built-in platform commands; false for broadcaster-authored ones.</summary>
    public bool IsPlatform { get; set; }

    public long UseCount { get; set; }

    public DateTime? LastUsedAt { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}

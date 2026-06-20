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

    public string? PipelineJson { get; set; }

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

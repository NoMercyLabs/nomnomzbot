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

namespace NomNomzBot.Domain.Widgets.Entities;

public class Widget : SoftDeletableEntity, ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public Guid BroadcasterId { get; set; }

    [MaxLength(255)]
    public string Name { get; set; } = null!;

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(20)]
    public string Version { get; set; } = "1.0.0";

    [MaxLength(20)]
    public string Framework { get; set; } = "vanilla";

    public bool IsEnabled { get; set; } = true;

    [MaxLength(100)]
    public string? TemplateId { get; set; }

    public List<string> EventSubscriptions { get; set; } = [];
    public Dictionary<string, object> Settings { get; set; } = new();

    public string? CustomCode { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}

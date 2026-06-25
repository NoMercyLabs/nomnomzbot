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
/// Persistent cross-command counter backing the <c>{{count.&lt;name&gt;}}</c> template
/// variable and the <c>set_counter</c> / <c>adjust_counter</c> pipeline actions.
/// Schema: G.4 (commands-pipelines.md §1).
/// </summary>
public class NamedCounter : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; }

    public Guid BroadcasterId { get; set; }

    /// <summary>Unique key within the broadcaster's channel (e.g. "deaths", "wins").</summary>
    [MaxLength(50)]
    public string Key { get; set; } = null!;

    public long Value { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}

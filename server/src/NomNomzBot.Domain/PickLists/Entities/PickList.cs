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

namespace NomNomzBot.Domain.PickLists.Entities;

/// <summary>
/// A generic named pick-list: a channel-scoped, uniquely-named bag of template strings that a pipeline action
/// or the <c>{list.pick.&lt;name&gt;}</c> template variable draws a random entry from. This is the single reusable
/// primitive behind behaviours like a <c>!fight</c> line picker or per-viewer shoutout variations — there is no
/// bespoke per-behaviour table; a streamer composes those from this one building block plus example presets.
/// </summary>
public class PickList : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; }
    public Guid BroadcasterId { get; set; }

    /// <summary>The list key, unique per channel (e.g. <c>fight_moves</c>). Used verbatim in <c>{list.pick.&lt;name&gt;}</c>.</summary>
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    /// <summary>Optional human-readable description shown in the dashboard.</summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// The pickable entries. Each entry may itself contain template placeholders (e.g. <c>{user} bonks {target}</c>)
    /// which are resolved when the entry is picked. Stored as a single JSON column (<c>text[]</c> on Postgres, a
    /// JSON <c>TEXT</c> column on SQLite) — no per-item table.
    /// </summary>
    public List<string> Items { get; set; } = [];

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}

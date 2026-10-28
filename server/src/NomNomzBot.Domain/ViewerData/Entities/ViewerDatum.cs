// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.ViewerData.Entities;

/// <summary>
/// Writable per-viewer key/value datum — the per-viewer sibling of <see cref="Commands.Entities.NamedCounter"/>
/// (per-viewer-data.md D1, schema G.14). One row per <c>(BroadcasterId, ViewerUserId, Key)</c>; the value is a
/// string, and the numeric pipeline ops (<c>adjust_viewer_data</c>) parse/format it as <see cref="long"/>, so one
/// table covers per-viewer flags/strings AND per-viewer counters. Backs <c>{viewer.data.&lt;key&gt;}</c> /
/// <c>{target.data.&lt;key&gt;}</c> template variables.
/// </summary>
public class ViewerDatum : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; }

    public Guid BroadcasterId { get; set; }

    public Guid ViewerUserId { get; set; }

    /// <summary>Slug key, unique per viewer within the channel (e.g. "deaths", "favorite_game").</summary>
    [MaxLength(50)]
    public string Key { get; set; } = null!;

    /// <summary>String value; numeric ops parse/format as <see cref="long"/>. Bounded (D5).</summary>
    [MaxLength(500)]
    public string Value { get; set; } = null!;

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;

    [ForeignKey(nameof(ViewerUserId))]
    public virtual User Viewer { get; set; } = null!;
}

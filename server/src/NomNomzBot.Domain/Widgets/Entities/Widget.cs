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

/// <summary>
/// A channel's overlay widget (schema §P.6). The authored source and its compiled bundle do NOT live here —
/// they live on append-only <see cref="WidgetVersion"/> rows; <see cref="ActiveVersionId"/> points at the version
/// the overlay currently serves. A widget is one of three <see cref="Source"/> kinds: <c>custom</c> (self-authored,
/// the only output of create/clone — maps to the fail-closed <c>unverified</c> trust tier), <c>verified_gallery</c>
/// (installed from a reviewed community gallery item), or <c>first_party</c> (the seeded catalogue).
/// </summary>
public class Widget : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; set; }

    [MaxLength(255)]
    public string Name { get; set; } = null!;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary><c>vue</c> | <c>react</c> | <c>svelte</c> | <c>vanilla</c> — the source language the build compiles.</summary>
    [MaxLength(20)]
    public string Framework { get; set; } = "vanilla";

    /// <summary><c>first_party</c> | <c>verified_gallery</c> | <c>custom</c> — drives the render-time CSP trust tier.</summary>
    [MaxLength(20)]
    public string Source { get; set; } = "custom";

    /// <summary>The gallery item this widget was installed from (null for a self-authored <c>custom</c> widget).</summary>
    public Guid? GalleryItemId { get; set; }

    /// <summary>The <see cref="WidgetVersion"/> the overlay currently serves; null until the first successful build.</summary>
    public Guid? ActiveVersionId { get; set; }

    public List<string> EventSubscriptions { get; set; } = [];
    public Dictionary<string, object> Settings { get; set; } = new();

    public bool IsEnabled { get; set; } = true;

    /// <summary>Last runtime fault the overlay reported for this widget (audit B5); null when healthy.</summary>
    public string? LastRuntimeError { get; set; }

    /// <summary>When the overlay last reported this widget running (audit B5).</summary>
    public DateTime? LastRanAt { get; set; }

    /// <summary>Config-schema generation for forward-compatible settings migration (defaults to 1).</summary>
    public int ConfigSchemaVersion { get; set; } = 1;

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}

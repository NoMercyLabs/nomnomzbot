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

namespace NomNomzBot.Domain.Marketplace.Entities;

/// <summary>
/// One installed content bundle (marketplace.md D6, schema H.11) — the record of WHAT a bundle import
/// created, so the install can later be updated (re-import) or uninstalled (remove exactly its entities).
/// Local ZIP installs have a null <see cref="MarketplaceItemId"/>; marketplace installs carry the item id
/// so re-installing the same item updates rather than duplicates. Soft-delete, tenant-scoped.
/// </summary>
public class InstalledBundle : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; set; }

    [MaxLength(150)]
    public string Name { get; set; } = null!;

    /// <summary><c>local</c> | <c>marketplace</c> — where the bundle ZIP came from.</summary>
    [MaxLength(20)]
    public string Source { get; set; } = "local";

    /// <summary>The marketplace catalog item id; null for local ZIP installs.</summary>
    [MaxLength(64)]
    public string? MarketplaceItemId { get; set; }

    [MaxLength(40)]
    public string Version { get; set; } = null!;

    [MaxLength(100)]
    public string? Author { get; set; }

    [MaxLength(40)]
    public string? License { get; set; }

    /// <summary>The bundle's <c>BundleManifest</c> as JSON — the authoritative record of what was in the ZIP.</summary>
    public string ManifestJson { get; set; } = null!;

    /// <summary>JSON map <c>{ type → Guid[] }</c> of the entity ids the import created — the uninstall/update key.</summary>
    public string InstalledEntityIdsJson { get; set; } = "{}";

    public Guid InstalledByUserId { get; set; }

    // ── Navigations ─────────────────────────────────────────────────────────────

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;

    [ForeignKey(nameof(InstalledByUserId))]
    public virtual User InstalledByUser { get; set; } = null!;
}

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
/// Per-channel enable/disable + config overrides for a built-in platform command.
/// Schema: G.2a (commands-pipelines.md §1).
/// </summary>
public class ChannelBuiltinCommand : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; }
    public Guid BroadcasterId { get; set; }

    /// <summary>
    /// Stable registry key for the built-in — the BARE builtin key (e.g. "sr", "song", "points"),
    /// never bang-prefixed. Unique per broadcaster.
    /// </summary>
    [MaxLength(100)]
    public string BuiltinKey { get; set; } = null!;

    public bool IsEnabled { get; set; } = true;

    /// <summary>EF Core schema version.</summary>
    public int ConfigSchemaVersion { get; set; } = 1;

    /// <summary>
    /// Optional per-channel override of the built-in's default behaviour.
    /// Null = inherit all defaults. Fields inside are nullable = inherit from the default.
    /// </summary>
    public string? OverridesJson { get; set; }

    /// <summary>Which <c>PlatformContentDefinition</c> (Kind=<c>command</c>) this row was installed from; null
    /// for a row a seeder never stamped (platform-admin.md §3.3). Generalizes the
    /// <c>Widget.GalleryItemId</c>/<c>InstalledSourceRevision</c> staleness pattern to system commands.</summary>
    public Guid? PlatformSourceDefinitionId { get; set; }

    /// <summary>The <c>PlatformContentVersion.Version</c> installed.</summary>
    public int? PlatformSourceVersion { get; set; }

    /// <summary>The <c>ContentHash</c> at install/last-sync time — compared against this row's live content
    /// hash (of <see cref="OverridesJson"/>, canonicalized) to decide "untouched" for
    /// <c>update_in_place_where_untouched</c> publishes.</summary>
    [MaxLength(64)]
    public string? PlatformSourceHash { get; set; }

    /// <summary>When this row last received a platform-originated update.</summary>
    public DateTime? PlatformSourceSyncedAt { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}

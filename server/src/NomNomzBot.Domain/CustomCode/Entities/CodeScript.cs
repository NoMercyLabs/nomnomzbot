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

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.CustomCode.Entities;

/// <summary>
/// An authored custom-code script (schema H.5, soft-delete). The <see cref="CurrentVersionId"/> pointer is the
/// hot-swap seam — published versions are immutable (<see cref="CodeScriptVersion"/>), and repointing this swaps
/// the live code with no row mutation. One script per <c>(channel, name)</c>.
/// </summary>
public class CodeScript : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Language { get; set; } = "typescript";

    /// <summary>The active version (hot-swap pointer); null until a valid version is published.</summary>
    public Guid? CurrentVersionId { get; set; }
    public bool IsEnabled { get; set; }
    public Guid? AuthorUserId { get; set; }
    public string? LastRuntimeError { get; set; }
    public DateTime? LastRanAt { get; set; }

    /// <summary>Which <c>PlatformContentDefinition</c> (Kind=<c>code_script</c>) this row was installed/seeded
    /// from; null for a script never installed via the platform-content spine (platform-admin.md §3.3, S-ADMIN-2e).
    /// The same provenance shape already applied to <c>ChannelBuiltinCommand</c>, <c>Widget</c> and
    /// <c>Pipeline</c>.</summary>
    public Guid? PlatformSourceDefinitionId { get; set; }

    /// <summary>The <c>PlatformContentVersion.Version</c> installed.</summary>
    public int? PlatformSourceVersion { get; set; }

    /// <summary>The <c>ContentHash</c> at install/last-sync time — compared against the canonicalized hash
    /// of this row's live current version's source to decide "untouched" for
    /// <c>update_in_place_where_untouched</c> publishes.</summary>
    public string? PlatformSourceHash { get; set; }

    /// <summary>When this row last received a platform-originated update.</summary>
    public DateTime? PlatformSourceSyncedAt { get; set; }
}

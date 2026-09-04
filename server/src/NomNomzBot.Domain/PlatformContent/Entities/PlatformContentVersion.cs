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

namespace NomNomzBot.Domain.PlatformContent.Entities;

/// <summary>
/// One append-only, immutable-once-published revision of a <see cref="PlatformContentDefinition"/>
/// (platform-admin.md §3.2) — the same append-only-version shape already used by <c>WidgetVersion</c> and
/// <c>CodeScriptVersion</c>. Drafting never touches a tenant row; only <c>publish</c> (§2.1) does.
/// </summary>
public class PlatformContentVersion
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid DefinitionId { get; set; }

    [ForeignKey(nameof(DefinitionId))]
    public virtual PlatformContentDefinition Definition { get; set; } = null!;

    /// <summary>Monotonic per definition, starting at 1.</summary>
    public int Version { get; set; }

    /// <summary>SHA-256 (lowercase hex) of the canonicalized <see cref="PayloadJson"/> — the value compared
    /// against a tenant's stored provenance hash to decide "untouched" (§2.1).</summary>
    [MaxLength(64)]
    public string ContentHash { get; set; } = null!;

    /// <summary>Kind-shaped payload (§3.2). For <c>command</c>: the <c>ChannelBuiltinCommand.OverridesJson</c>-
    /// shaped default configuration for the builtin named by <c>PlatformContentDefinition.Key</c>.</summary>
    public string PayloadJson { get; set; } = null!;

    /// <summary>Widget kind only — asset ids of the captured render-gallery screenshots for this version.
    /// Empty (never null) when not applicable — the JSON-column convention (<c>[VC:JSON]</c>,
    /// <c>JsonValueConverter</c>) round-trips null as an empty collection.</summary>
    public List<string> RenderGalleryRefs { get; set; } = [];

    /// <summary>Free-text changelog entry, required when the publish <c>Mode</c> is <c>force</c>.</summary>
    [MaxLength(2000)]
    public string? PublishNote { get; set; }

    public DateTime DraftedAt { get; set; }

    public Guid DraftedByPrincipalId { get; set; }

    /// <summary>Null while still a draft.</summary>
    public DateTime? PublishedAt { get; set; }

    public Guid? PublishedByPrincipalId { get; set; }
}

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

namespace NomNomzBot.Domain.PlatformContent.Entities;

/// <summary>
/// One shipped, platform-authored piece of content (a system command, first-party widget, system pipeline,
/// or code script) — the natural-key anchor that owns an append-only sequence of
/// <see cref="PlatformContentVersion"/> rows (platform-admin.md §2.2, §3.1). NOT <c>ITenantScoped</c> —
/// platform content is global by design; a tenant's own row (e.g. <c>ChannelBuiltinCommand</c>) carries a
/// nullable provenance pointer back to the version it was installed from (§3.3), never a live FK to this row.
/// SaaS-only (platform-employee surface, §0 marker).
/// </summary>
public class PlatformContentDefinition
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>One of <see cref="PlatformContentKinds"/>. Only <c>command</c> is installable in this slice —
    /// <c>widget</c>/<c>pipeline</c>/<c>code_script</c> are carried by the discriminator for follow-up slices.</summary>
    [MaxLength(20)]
    public string Kind { get; set; } = null!;

    /// <summary>Natural key within <see cref="Kind"/> (e.g. the <c>ChannelBuiltinCommand.BuiltinKey</c> "sr").
    /// Unique per <see cref="Kind"/>.</summary>
    [MaxLength(100)]
    public string Key { get; set; } = null!;

    [MaxLength(200)]
    public string DisplayName { get; set; } = null!;

    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>The latest PUBLISHED version. Null until the first publish.</summary>
    public Guid? CurrentVersionId { get; set; }

    /// <summary>The newest version regardless of publish state (may equal <see cref="CurrentVersionId"/>).</summary>
    public Guid? LatestDraftVersionId { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid CreatedByPrincipalId { get; set; }

    /// <summary>Soft-retire: stops future installs; never touches already-installed tenant copies (the
    /// seeder principle's "never overwrite built content" applied to removal, §3.1).</summary>
    public DateTime? RetiredAt { get; set; }
}

/// <summary>The closed set of platform content kinds (§3.1). A definition's kind is one of exactly these.</summary>
public static class PlatformContentKinds
{
    public const string Command = "command";
    public const string Widget = "widget";
    public const string Pipeline = "pipeline";
    public const string CodeScript = "code_script";

    public static bool IsKnown(string? kind) => kind is Command or Widget or Pipeline or CodeScript;

    public static IReadOnlyList<string> All { get; } = [Command, Widget, Pipeline, CodeScript];
}

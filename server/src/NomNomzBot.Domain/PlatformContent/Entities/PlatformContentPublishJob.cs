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
/// One append-only row per publish attempt (platform-admin.md §3.4) — the fan-out record that walks tenant
/// rows per the chosen <see cref="Mode"/> and re-copies the version's payload. Never a schema migration,
/// never a live cross-tenant read (§2.2).
/// </summary>
public class PlatformContentPublishJob
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid DefinitionId { get; set; }

    /// <summary>Null for a first publish.</summary>
    public int? FromVersion { get; set; }

    public int ToVersion { get; set; }

    /// <summary>One of <see cref="PlatformContentPublishModes"/>.</summary>
    [MaxLength(40)]
    public string Mode { get; set; } = null!;

    public Guid RequestedByPrincipalId { get; set; }

    public DateTime RequestedAt { get; set; }

    /// <summary>From the <c>publish-preview</c> call this publish was confirmed against.</summary>
    public int PreviewAffectedCount { get; set; }

    /// <summary>Tenants skipped because their copy was edited (modes 2-3 only).</summary>
    public int PreviewSkippedCount { get; set; }

    /// <summary>Actual count once the fan-out completes — compared against <see cref="PreviewAffectedCount"/>
    /// as a drift check.</summary>
    public int? ConfirmedAffectedCount { get; set; }

    /// <summary>One of <see cref="PlatformContentPublishJobStatuses"/>.</summary>
    [MaxLength(20)]
    public string Status { get; set; } = PlatformContentPublishJobStatuses.Running;

    public DateTime? CompletedAt { get; set; }

    [MaxLength(2000)]
    public string? FailureReason { get; set; }

    /// <summary>Widget kind only (S-ADMIN-2c-b): tenant <c>Widget</c> rows whose compiled-bundle rebuild failed
    /// during this fan-out. Their PREVIOUS successful <c>WidgetVersion</c>/bundle stays live — a rebuild failure
    /// never blanks a working overlay — so this is the only record of which tenants did not receive the fix; an
    /// admin re-runs the publish (or investigates) using this list. Empty (never null) when nothing failed, or
    /// for a <c>command</c>-kind job.</summary>
    public List<Guid> RebuildFailedWidgetIds { get; set; } = [];

    /// <summary>Pipeline kind only: tenant <c>Pipeline</c> rows whose new graph failed
    /// <c>ICommandConfigValidator</c> validation during this fan-out. Their PREVIOUS working graph (and
    /// <c>PipelineStep</c> rows) stay live — a validation failure never leaves a tenant with a broken
    /// pipeline — so this is the only record of which tenants did not receive the update; an admin
    /// investigates the broken version using this list. Empty (never null) when nothing failed, or for a
    /// non-<c>pipeline</c>-kind job.</summary>
    public List<Guid> ValidationFailedPipelineIds { get; set; } = [];
}

/// <summary>The closed set of publish modes (§2.1). A publish job's mode is one of exactly these.</summary>
public static class PlatformContentPublishModes
{
    /// <summary>Creates a new installable definition/version; existing tenant rows are completely untouched.
    /// Zero blast radius by construction.</summary>
    public const string PublishAsNew = "publish_as_new";

    /// <summary>Updates only tenant rows whose current content hash still equals their stored provenance
    /// hash — a tenant who customized their copy keeps their customization.</summary>
    public const string UpdateInPlaceWhereUntouched = "update_in_place_where_untouched";

    /// <summary>Updates every installed tenant row regardless of local edits. Gated by its own Critical-tier
    /// action key, separate from <c>content:publish</c>, because it is the one mode capable of destroying
    /// tenant work.</summary>
    public const string Force = "force";

    public static bool IsKnown(string? mode) =>
        mode is PublishAsNew or UpdateInPlaceWhereUntouched or Force;

    public static IReadOnlyList<string> All { get; } =
    [PublishAsNew, UpdateInPlaceWhereUntouched, Force];
}

/// <summary>The closed set of publish job statuses. A job's status is one of exactly these.</summary>
public static class PlatformContentPublishJobStatuses
{
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";

    public static IReadOnlyList<string> All { get; } = [Running, Completed, Failed];
}

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

namespace NomNomzBot.Domain.Moderation.Entities;

/// <summary>
/// One viewer-filed report about another chatter, awaiting moderator triage (moderation.md J.8). A viewer submits
/// it (Gate-1) with a reason; moderators list the open queue and resolve each — <c>dismissed</c> (no action) or
/// <c>escalated</c> (a mod then acts via the normal ban/timeout tools). The report is a truthful record of what was
/// reported and how it was resolved; it never enforces anything itself. One-per-file — a viewer re-reporting the
/// same person just adds another open row (the queue de-dupes visually, mods decide).
/// </summary>
public class ViewerReport : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; }

    public Guid BroadcasterId { get; set; }

    /// <summary>The reported chatter's internal user id (a get-or-create Users row — viewers ARE users).</summary>
    public Guid ReportedUserId { get; set; }

    /// <summary>The reported chatter's raw Twitch id, snapshotted so the row survives an identity change.</summary>
    [MaxLength(50)]
    public string ReportedTwitchUserId { get; set; } = null!;

    /// <summary>The reporting viewer's internal user id; null for an anonymous/system-filed report.</summary>
    public Guid? ReporterUserId { get; set; }

    /// <summary>Why they were reported — the viewer's free text (bounded).</summary>
    [MaxLength(500)]
    public string Reason { get; set; } = null!;

    /// <summary>Queue state: <c>open</c> → <c>triaged</c> / <c>dismissed</c> / <c>escalated</c>.</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "open";

    /// <summary>The moderator who resolved it (null while still open).</summary>
    public Guid? ResolvedByUserId { get; set; }

    public DateTime? ResolvedAt { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;

    [ForeignKey(nameof(ReportedUserId))]
    public virtual User ReportedUser { get; set; } = null!;
}

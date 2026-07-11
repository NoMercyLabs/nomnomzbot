// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations.Schema;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.MediaShare.Entities;

/// <summary>
/// Per-channel media-share configuration (media-share.md L.10). Safe-by-default: approval on, a closed
/// embeddable source set, a hard duration cap. One row per channel.
/// </summary>
public class MediaShareConfig : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; }

    public Guid BroadcasterId { get; set; }

    public bool IsEnabled { get; set; }

    /// <summary>Submissions enter <c>pending</c> until a mod approves (D1). Default on.</summary>
    public bool RequireApproval { get; set; } = true;

    public bool AllowTwitchClips { get; set; } = true;

    public bool AllowYouTube { get; set; } = true;

    /// <summary>Hard cap; over-length submissions are rejected at submit time (D3, default 180).</summary>
    public int MaxDurationSeconds { get; set; } = 180;

    /// <summary>Loyalty-point entry cost; null/0 = free (D4).</summary>
    public long? EntryCost { get; set; }

    /// <summary>JSON eligibility rules (sub-only / min-standing / min-account-age); null = everyone (D4).</summary>
    public string? EligibilityJson { get; set; }

    public int MaxQueueLength { get; set; } = 20;

    public int PerUserCooldownSeconds { get; set; } = 60;

    public int ConfigSchemaVersion { get; set; } = 1;

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}

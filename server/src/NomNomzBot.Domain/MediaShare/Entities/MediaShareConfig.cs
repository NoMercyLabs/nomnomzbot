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

    /// <summary>Off until the streamer turns it on. A channel with no row resolves to this default.</summary>
    public bool IsEnabled { get; set; } = false;

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

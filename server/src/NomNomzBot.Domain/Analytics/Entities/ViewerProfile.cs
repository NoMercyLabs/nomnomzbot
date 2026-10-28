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

namespace NomNomzBot.Domain.Analytics.Entities;

/// <summary>
/// Per-viewer-per-channel aggregate profile (schema M.1) — the analytics anonymization anchor + FK root for the
/// detailed read models. Rebuilt by <c>ViewerProfileProjection</c> folding the viewer's journaled activity. GDPR
/// erasure scrubs the PII snapshots here while the no-PII channel aggregate (M.8) survives. Soft-deletable.
/// </summary>
public class ViewerProfile : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; set; }
    public Guid ViewerUserId { get; set; }
    public string ViewerTwitchUserId { get; set; } = null!;
    public string? UsernameSnapshot { get; set; }
    public string? DisplayNameSnapshot { get; set; }
    public DateTime? FirstSeenAt { get; set; }
    public DateTime? LastSeenAt { get; set; }

    public long TotalWatchSeconds { get; set; }
    public long TotalMessages { get; set; }
    public long TotalCommandsUsed { get; set; }
    public long TotalRedemptions { get; set; }
    public long TotalSongRequests { get; set; }

    public bool IsFollower { get; set; }
    public bool IsSubscriber { get; set; }
    public string? SubTier { get; set; }
    public bool IsAnalyticsOptedOut { get; set; }
}

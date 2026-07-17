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
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Moderation.Entities;

/// <summary>
/// The per-viewer trust + heat pair (moderation.md §3.8, schema J.5) — a PROJECTION recomputed on every
/// moderation signal. <c>TrustScore</c> is the long-term signal (the shared calculator);
/// <c>HeatScore</c> is 0–100 recent-violation pressure with exponential decay (half-life 24 h). Unique per
/// (channel, subject).
/// </summary>
public class UserTrustScore : BaseEntity, ITenantScoped
{
    public long Id { get; set; }

    public Guid BroadcasterId { get; set; }

    public Guid SubjectUserId { get; set; }

    [MaxLength(50)]
    public string SubjectTwitchUserId { get; set; } = null!;

    public decimal TrustScore { get; set; }

    public decimal HeatScore { get; set; }

    public DateTime? LastHeatEventAt { get; set; }

    public DateTime ComputedAt { get; set; }
}

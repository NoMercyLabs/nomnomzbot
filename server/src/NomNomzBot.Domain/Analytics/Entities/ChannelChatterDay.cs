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

namespace NomNomzBot.Domain.Analytics.Entities;

/// <summary>
/// The per-day distinctness + presence anchor behind the M.8 channel daily aggregate — owned and reset by
/// <c>ChannelAnalyticsDailyProjection</c> alongside <see cref="ChannelAnalyticsDaily"/>. One row per
/// (channel, day, viewer-hash): the first presence event mints it (and, for a chat message, increments
/// <c>UniqueChatters</c> exactly once), and each later presence event advances <see cref="LastSeenAt"/>,
/// folding the delta into <c>TotalWatchSeconds</c> — the same first→last-activity span semantics as the
/// M.2 watch sessions. Carries only a keyed HASH of the viewer's platform identity (never the id itself),
/// so the aggregate keeps its "survives any viewer erasure, no PII" guarantee.
/// </summary>
public class ChannelChatterDay : ITenantScoped
{
    public long Id { get; set; }
    public Guid BroadcasterId { get; set; }
    public DateOnly ActivityDate { get; set; }

    /// <summary>SHA-256 (hex) of <c>{provider}:{externalUserId}</c> — distinctness without identity.</summary>
    [MaxLength(64)]
    public string ChatterHash { get; set; } = null!;

    /// <summary>True once the viewer has CHATTED that day (commands/redemptions alone are presence, not chat).</summary>
    public bool Chatted { get; set; }

    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }

    /// <summary>
    /// The live stream covering the last presence event (null when it happened offline). Watch time only
    /// accrues between two presence events inside the SAME stream — never across streams or offline gaps.
    /// </summary>
    [MaxLength(50)]
    public string? LastStreamId { get; set; }
}

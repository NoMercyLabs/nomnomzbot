// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Analytics.Entities;

/// <summary>
/// Per-viewer-per-day engagement roll-up (schema M.7) — powers charts/time-series without scanning raw logs. One
/// upserted row per <c>(channel, viewer, channel-local date)</c>, folded from chat / command / redemption / song-
/// request / currency / game events.
/// </summary>
public class ViewerEngagementDaily : ITenantScoped
{
    public long Id { get; set; }
    public Guid BroadcasterId { get; set; }
    public Guid ViewerProfileId { get; set; }
    public Guid ViewerUserId { get; set; }
    public DateOnly ActivityDate { get; set; }
    public long WatchSeconds { get; set; }
    public int MessageCount { get; set; }
    public int CommandCount { get; set; }
    public int RedemptionCount { get; set; }
    public int SongRequestCount { get; set; }
    public long CurrencyEarned { get; set; }
    public long CurrencySpent { get; set; }
    public int GamesPlayed { get; set; }
}

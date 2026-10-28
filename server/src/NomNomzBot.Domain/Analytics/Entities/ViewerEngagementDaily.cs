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

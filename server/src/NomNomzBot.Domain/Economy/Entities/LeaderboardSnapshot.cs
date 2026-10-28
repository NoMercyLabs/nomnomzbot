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

namespace NomNomzBot.Domain.Economy.Entities;

/// <summary>
/// One frozen rank in a closed leaderboard period (economy.md L.3) — the historical record after a period
/// closes. APPEND-ONLY: no <c>UpdatedAt</c>/<c>DeletedAt</c>, keyed by a <c>long</c> identity. Snapshots the
/// subject + display name + value so a later rename or data change never rewrites history.
/// </summary>
public class LeaderboardSnapshot
{
    public long Id { get; set; }
    public Guid LeaderboardConfigId { get; set; }
    public Guid? BroadcasterId { get; set; }
    public string PeriodKey { get; set; } = null!;
    public int Rank { get; set; }
    public Guid? SubjectAccountId { get; set; }
    public Guid? SubjectUserId { get; set; }
    public string SubjectTwitchUserId { get; set; } = null!;
    public string DisplayNameSnapshot { get; set; } = null!;
    public long Value { get; set; }
    public DateTime CapturedAt { get; set; }
}

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
/// Channel-level daily aggregate (schema M.8) — pure counts, NO PII, so it survives any viewer erasure. One
/// upserted row per <c>(channel, channel-local date)</c>, folded from every activity + follow/subscribe/cheer +
/// presence. The exit-critical "projections rebuild from the journal" capability lands here.
/// </summary>
public class ChannelAnalyticsDaily : ITenantScoped
{
    public long Id { get; set; }
    public Guid BroadcasterId { get; set; }
    public DateOnly ActivityDate { get; set; }
    public int UniqueChatters { get; set; }
    public long TotalMessages { get; set; }
    public long TotalWatchSeconds { get; set; }
    public int NewFollowers { get; set; }
    public int NewSubscribers { get; set; }
    public long BitsCheered { get; set; }
    public long CommandsRun { get; set; }
    public long RedemptionsCount { get; set; }
    public int SongRequests { get; set; }
    public long CurrencyEarnedTotal { get; set; }
    public long CurrencySpentTotal { get; set; }
    public int GamesPlayed { get; set; }
    public int? PeakViewers { get; set; }
}

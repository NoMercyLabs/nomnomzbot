// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.Analytics;

/// <summary>Per-channel analytics reads (analytics.md §3.3) over the projected daily roll-ups.</summary>
public interface IChannelAnalyticsService
{
    /// <summary>The channel daily aggregate series (M.8) over a date range — the chart/time-series source.</summary>
    Task<Result<IReadOnlyList<ChannelAnalyticsDailyDto>>> GetDailySeriesAsync(
        Guid broadcasterId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default
    );

    /// <summary>Headline totals over a range + deltas vs the preceding equal-length window (folds M.8).</summary>
    Task<Result<ChannelAnalyticsSummaryDto>> GetSummaryAsync(
        Guid broadcasterId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default
    );

    /// <summary>Top viewers over a range by a chosen metric (folds M.7) — respects M.1 analytics opt-out.</summary>
    Task<Result<IReadOnlyList<TopViewerDto>>> GetTopViewersAsync(
        Guid broadcasterId,
        TopViewerMetric metric,
        DateOnly from,
        DateOnly to,
        int top,
        CancellationToken ct = default
    );
}

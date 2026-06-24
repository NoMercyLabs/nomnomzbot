// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.network

import kotlinx.serialization.Serializable

// The typed analytics facade — the channel's headline totals the Analytics page renders. Real data only:
// the backend folds these from the per-day analytics rows (analytics.md §4), nothing fabricated. State
// holders depend on this interface and fake it in tests without HTTP.
//
// Backend route (AnalyticsController, channel/management plane):
//   GET /api/v1/channels/{channelId}/analytics/channel/summary?from=YYYY-MM-DD&to=YYYY-MM-DD
//     →  StatusResponseDto<ChannelAnalyticsSummaryDto>
// The range is required and validated server-side (from <= to, span <= 366 days); the caller passes
// inclusive `yyyy-MM-dd` dates.
interface AnalyticsApi {
    /** The channel's headline totals over the inclusive `[from, to]` day range. */
    suspend fun summary(channelId: String, from: String, to: String): ApiResult<AnalyticsSummary>
}

class RestAnalyticsApi(private val client: ApiClient) : AnalyticsApi {
    override suspend fun summary(
        channelId: String,
        from: String,
        to: String,
    ): ApiResult<AnalyticsSummary> =
        client.getEnvelope("api/v1/channels/$channelId/analytics/channel/summary?from=$from&to=$to")
}

/**
 * The channel analytics summary (backend `ChannelAnalyticsSummaryDto`): headline totals over a range. The
 * page renders the scalar counters as stat tiles; the nested `deltas` object is read by a later slice, so it
 * is intentionally omitted here (the client deliberately reads a subset — unknown keys are ignored).
 */
@Serializable
data class AnalyticsSummary(
    val totalMessages: Long = 0,
    val newFollowers: Int = 0,
    val newSubscribers: Int = 0,
    val bitsCheered: Long = 0,
    val commandsRun: Long = 0,
    val redemptionsCount: Long = 0,
    val songRequests: Int = 0,
    val currencyEarnedTotal: Long = 0,
    val currencySpentTotal: Long = 0,
    /** Peak concurrent viewers across the range, or null when the range has no recorded days. */
    val peakViewers: Int? = null,
)

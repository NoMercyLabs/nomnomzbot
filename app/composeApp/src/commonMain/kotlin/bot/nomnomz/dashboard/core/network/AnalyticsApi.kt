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

// The typed analytics facade — the channel analytics the Analytics page renders. Real data only:
// the backend folds these from the per-day analytics rows (analytics.md §4), nothing fabricated. State
// holders depend on this interface and fake it in tests without HTTP.
//
// Backend routes (AnalyticsController, channel/management plane):
//   GET /api/v1/channels/{channelId}/analytics/channel/summary?from=…&to=…
//   GET /api/v1/channels/{channelId}/analytics/channel/daily?from=…&to=…
//   GET /api/v1/channels/{channelId}/analytics/channel/top-viewers?metric=…&from=…&to=…&top=…
// The range is required and validated server-side (from <= to, span <= 366 days); the caller passes
// inclusive `yyyy-MM-dd` dates.
interface AnalyticsApi {
    /** The channel's headline totals over the inclusive `[from, to]` day range. */
    suspend fun summary(channelId: String, from: String, to: String): ApiResult<AnalyticsSummary>

    /** Per-day time-series over the inclusive `[from, to]` range (used for trend charts). */
    suspend fun daily(channelId: String, from: String, to: String): ApiResult<List<DailyMetricRow>>

    /** Top-N viewers ranked by [metric] (`Messages`, `WatchSeconds`, `Commands`, etc.) over the range. */
    suspend fun topViewers(
        channelId: String,
        metric: String,
        from: String,
        to: String,
        top: Int,
    ): ApiResult<List<TopViewerEntry>>

    /** One viewer's full analytics profile (messages, watch time, follower/sub status, opt-out flag). */
    suspend fun viewerProfile(channelId: String, viewerUserId: String): ApiResult<ViewerAnalyticsProfile>

    /** One viewer's watch streak for the channel. */
    suspend fun viewerStreak(channelId: String, viewerUserId: String): ApiResult<WatchStreak>

    /** Toggle the analytics opt-out for [viewerUserId] (self or moderator). */
    suspend fun setAnalyticsOptOut(
        channelId: String,
        viewerUserId: String,
        optedOut: Boolean,
    ): ApiResult<Unit>
}

class RestAnalyticsApi(private val client: ApiClient) : AnalyticsApi {
    override suspend fun summary(
        channelId: String,
        from: String,
        to: String,
    ): ApiResult<AnalyticsSummary> =
        client.getEnvelope("api/v1/channels/$channelId/analytics/channel/summary?from=$from&to=$to")

    override suspend fun daily(
        channelId: String,
        from: String,
        to: String,
    ): ApiResult<List<DailyMetricRow>> =
        client.getEnvelope("api/v1/channels/$channelId/analytics/channel/daily?from=$from&to=$to")

    override suspend fun topViewers(
        channelId: String,
        metric: String,
        from: String,
        to: String,
        top: Int,
    ): ApiResult<List<TopViewerEntry>> =
        client.getEnvelope(
            "api/v1/channels/$channelId/analytics/channel/top-viewers?metric=$metric&from=$from&to=$to&top=$top"
        )

    override suspend fun viewerProfile(
        channelId: String,
        viewerUserId: String,
    ): ApiResult<ViewerAnalyticsProfile> =
        client.getEnvelope("api/v1/channels/$channelId/analytics/viewers/$viewerUserId")

    override suspend fun viewerStreak(
        channelId: String,
        viewerUserId: String,
    ): ApiResult<WatchStreak> =
        client.getEnvelope("api/v1/channels/$channelId/analytics/viewers/$viewerUserId/streak")

    override suspend fun setAnalyticsOptOut(
        channelId: String,
        viewerUserId: String,
        optedOut: Boolean,
    ): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/analytics/viewers/$viewerUserId/opt-out",
            SetAnalyticsOptOutBody(optedOut),
        )
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

/** One day of the per-channel aggregate (backend `ChannelAnalyticsDailyDto`) — the time-series / chart row. */
@Serializable
data class DailyMetricRow(
    val activityDate: String = "",
    val uniqueChatters: Int = 0,
    val totalMessages: Long = 0,
    val newFollowers: Int = 0,
    val newSubscribers: Int = 0,
    val peakViewers: Int? = null,
)

/** A channel's top viewer over a range by the chosen metric (backend `TopViewerDto`). */
@Serializable
data class TopViewerEntry(
    val viewerUserId: String = "",
    val displayName: String? = null,
    val metricValue: Long = 0,
)

/** One viewer's full analytics profile (backend `ViewerProfileDto`) — the stats the Me screen shows. */
@Serializable
data class ViewerAnalyticsProfile(
    val viewerUserId: String = "",
    val displayName: String? = null,
    val totalWatchSeconds: Long = 0,
    val totalMessages: Long = 0,
    val totalCommandsUsed: Long = 0,
    val totalRedemptions: Long = 0,
    val isFollower: Boolean = false,
    val isSubscriber: Boolean = false,
    val subTier: String? = null,
    val isAnalyticsOptedOut: Boolean = false,
)

/** One viewer's watch streak for the channel (backend `WatchStreakDto`). */
@Serializable
data class WatchStreak(
    val currentStreak: Int = 0,
    val maxStreak: Int = 0,
    val lastSeenDate: String = "",
)

/** Request body for the analytics opt-out toggle (backend `SetAnalyticsOptOutRequest`). */
@Serializable
private data class SetAnalyticsOptOutBody(val optedOut: Boolean)

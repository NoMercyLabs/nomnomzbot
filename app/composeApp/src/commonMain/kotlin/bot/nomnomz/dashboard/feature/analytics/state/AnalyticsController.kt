// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.analytics.state

import bot.nomnomz.dashboard.core.network.AnalyticsApi
import bot.nomnomz.dashboard.core.network.AnalyticsSummary
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.DailyMetricRow
import bot.nomnomz.dashboard.core.network.StreamAnalytics
import bot.nomnomz.dashboard.core.network.StreamListItem
import bot.nomnomz.dashboard.core.network.TopViewerEntry
import bot.nomnomz.dashboard.core.network.ViewerAnalyticsProfile
import bot.nomnomz.dashboard.core.network.ViewerEngagementDay
import bot.nomnomz.dashboard.core.network.ViewerProfileListEntry
import bot.nomnomz.dashboard.core.network.ViewerProfilePage
import bot.nomnomz.dashboard.core.network.WatchStreak
import kotlin.time.Clock
import kotlin.time.ExperimentalTime
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Analytics page's state-holder (analytics.md §4 — the channel headline totals). Resolves the active
// channel, then loads its real summary over the trailing window from the backend (no fabricated counts). The
// screen renders [state]; a retry / reconnect calls [load] again.
class AnalyticsController(
    private val channelsApi: ChannelsApi,
    private val analyticsApi: AnalyticsApi,
) {
    private val _state: MutableStateFlow<AnalyticsState> = MutableStateFlow(AnalyticsState.Loading)

    /** The page render state: loading / ready (with the summary) / error. */
    val state: StateFlow<AnalyticsState> = _state.asStateFlow()

    private val _viewers: MutableStateFlow<ViewerListState> = MutableStateFlow(ViewerListState.Idle)

    /** The viewer drill-down list state (searchable, sortable, paginated). Loaded lazily by [loadViewers]. */
    val viewers: StateFlow<ViewerListState> = _viewers.asStateFlow()

    private val _viewerDetail: MutableStateFlow<ViewerDetailState?> = MutableStateFlow(null)

    /** The open per-viewer profile, or null when the drill-down list (not a single viewer) is showing. */
    val viewerDetail: StateFlow<ViewerDetailState?> = _viewerDetail.asStateFlow()

    // Resolved on first load, reused by [selectStream] so a stream switch never re-resolves the channel.
    private var channelId: String? = null

    // The viewer-list query the drill-down sits on — the "stuck on page 1" fix for the viewer table.
    private var viewerSearch: String? = null
    private var viewerSort: String = ViewerSort.Watch
    private var viewerPage: Int = 1

    /** Resolve the active channel, then load summary + daily trends + top viewers + stream history concurrently. */
    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch after a mutation keeps
        // the current content on screen (no flash) and swaps it when the new data arrives.
        if (_state.value !is AnalyticsState.Ready) _state.value = AnalyticsState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = AnalyticsState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        channelId = channel.id

        val to: String = today()
        val from: String = daysBefore(to, WINDOW_DAYS - 1)

        coroutineScope {
            val summaryDeferred =
                async { analyticsApi.summary(channel.id, from = from, to = to) }
            val dailyDeferred =
                async { analyticsApi.daily(channel.id, from = from, to = to) }
            val topDeferred =
                async {
                    analyticsApi.topViewers(
                        channel.id,
                        metric = "Messages",
                        from = from,
                        to = to,
                        top = 10,
                    )
                }
            val streamsDeferred =
                async { analyticsApi.streams(channel.id) }

            val summaryResult: ApiResult<AnalyticsSummary> = summaryDeferred.await()
            val dailyResult: ApiResult<List<DailyMetricRow>> = dailyDeferred.await()
            val topResult: ApiResult<List<TopViewerEntry>> = topDeferred.await()
            val streamsResult: ApiResult<List<StreamListItem>> = streamsDeferred.await()

            if (summaryResult is ApiResult.Failure) {
                _state.value = AnalyticsState.Error(summaryResult.error.message)
                return@coroutineScope
            }

            _state.value =
                AnalyticsState.Ready(
                    summary = (summaryResult as ApiResult.Ok).value,
                    daily = (dailyResult as? ApiResult.Ok)?.value ?: emptyList(),
                    topViewers = (topResult as? ApiResult.Ok)?.value ?: emptyList(),
                    streams = (streamsResult as? ApiResult.Ok)?.value ?: emptyList(),
                )
        }
    }

    /**
     * Switch the stat view between all-time ([streamId] == null) and one specific stream. All-time clears the
     * per-stream detail; a stream id folds that stream's own numbers via the backend and shows them in place of
     * the all-time summary. The daily charts + top viewers stay on the trailing window (they are a range view).
     * A detail fetch failure surfaces on the Ready state without dropping the current content.
     */
    suspend fun selectStream(streamId: String?) {
        val current: AnalyticsState = _state.value
        if (current !is AnalyticsState.Ready) return

        if (streamId == null) {
            _state.value = current.copy(selectedStreamId = null, streamDetail = null, streamError = null)
            return
        }

        val channel: String = channelId ?: return
        _state.value = current.copy(selectedStreamId = streamId, streamError = null)
        when (val result: ApiResult<StreamAnalytics> = analyticsApi.streamDetail(channel, streamId)) {
            is ApiResult.Ok ->
                _state.value =
                    (_state.value as? AnalyticsState.Ready)?.copy(
                        selectedStreamId = streamId,
                        streamDetail = result.value,
                        streamError = null,
                    ) ?: return
            is ApiResult.Failure ->
                _state.value =
                    (_state.value as? AnalyticsState.Ready)?.copy(streamError = result.error.message) ?: return
        }
    }

    // ── Viewer drill-down ──────────────────────────────────────────────────────

    /** Resolve the channel if needed, then load the first page of the viewer analytics list. */
    suspend fun loadViewers() {
        viewerSearch = null
        viewerSort = ViewerSort.Watch
        viewerPage = 1
        fetchViewers(isInitial = true)
    }

    /** Set the viewer search fragment (blank clears it), reset to the first page, and reload. */
    suspend fun setViewerSearch(query: String) {
        viewerSearch = query.takeIf { it.isNotBlank() }
        viewerPage = 1
        fetchViewers(isInitial = false)
    }

    /** Switch the viewer-list sort (one of [ViewerSort]), reset to the first page, and reload. */
    suspend fun setViewerSort(sort: String) {
        viewerSort = sort
        viewerPage = 1
        fetchViewers(isInitial = false)
    }

    /** Advance to the next page of the viewer list. The screen only calls this while `hasMore` is true. */
    suspend fun nextViewersPage() {
        viewerPage += 1
        fetchViewers(isInitial = false)
    }

    /** Step back to the previous page of the viewer list. A no-op on the first page. */
    suspend fun prevViewersPage() {
        if (viewerPage <= 1) return
        viewerPage -= 1
        fetchViewers(isInitial = false)
    }

    private suspend fun fetchViewers(isInitial: Boolean) {
        val channel: String =
            ensureChannel()
                ?: run {
                    _viewers.value = ViewerListState.Error(NoChannelError)
                    return
                }
        if (isInitial || _viewers.value !is ViewerListState.Ready) _viewers.value = ViewerListState.Loading
        when (
            val result: ApiResult<ViewerProfilePage> =
                analyticsApi.listViewers(
                    channelId = channel,
                    search = viewerSearch,
                    sort = viewerSort,
                    followersOnly = null,
                    subscribersOnly = null,
                    page = viewerPage,
                    pageSize = VIEWER_PAGE_SIZE,
                )
        ) {
            is ApiResult.Failure -> _viewers.value = ViewerListState.Error(result.error.message)
            is ApiResult.Ok -> {
                val viewerPageResult: ViewerProfilePage = result.value
                _viewers.value =
                    ViewerListState.Ready(
                        viewers = viewerPageResult.data,
                        search = viewerSearch.orEmpty(),
                        sort = viewerSort,
                        page = viewerPage,
                        hasPrev = viewerPage > 1,
                        hasMore = viewerPageResult.hasMore,
                        total = viewerPageResult.total,
                    )
            }
        }
    }

    /**
     * Open [viewerUserId]'s profile drill-down: the full profile (required), plus the watch streak and the last
     * 30 days of daily engagement (both supplementary — a failure just leaves that part blank). [displayName] is
     * carried through for the header while the profile loads.
     */
    suspend fun openViewer(viewerUserId: String, displayName: String) {
        val channel: String = ensureChannel() ?: return
        _viewerDetail.value =
            ViewerDetailState(viewerUserId = viewerUserId, displayName = displayName, loading = true)

        val to: String = today()
        val from: String = daysBefore(to, WINDOW_DAYS - 1)

        val profile: ViewerAnalyticsProfile? =
            when (val result: ApiResult<ViewerAnalyticsProfile> = analyticsApi.viewerProfile(channel, viewerUserId)) {
                is ApiResult.Ok -> result.value
                is ApiResult.Failure -> null
            }
        val streak: WatchStreak? =
            when (val result: ApiResult<WatchStreak> = analyticsApi.viewerStreak(channel, viewerUserId)) {
                is ApiResult.Ok -> result.value
                is ApiResult.Failure -> null
            }
        val engagement: List<ViewerEngagementDay> =
            when (
                val result: ApiResult<List<ViewerEngagementDay>> =
                    analyticsApi.viewerEngagement(channel, viewerUserId, from = from, to = to)
            ) {
                is ApiResult.Ok -> result.value
                is ApiResult.Failure -> emptyList()
            }

        _viewerDetail.value =
            ViewerDetailState(
                viewerUserId = viewerUserId,
                displayName = displayName,
                loading = false,
                profile = profile,
                streak = streak,
                engagement = engagement,
                error = profile == null,
            )
    }

    /** Close the per-viewer drill-down and return to the viewer list. */
    fun closeViewer() {
        _viewerDetail.value = null
    }

    /**
     * Toggle the analytics opt-out for the open viewer (self or moderator), then re-read the profile so the flag
     * reflects the backend. Returns the error message on failure, or null on success. Role-gated by the screen.
     */
    suspend fun setViewerOptOut(optedOut: Boolean): String? {
        val detail: ViewerDetailState = _viewerDetail.value ?: return null
        val channel: String = ensureChannel() ?: return NoChannelError
        _viewerDetail.value = detail.copy(optOutBusy = true)
        val error: String? =
            when (
                val result: ApiResult<Unit> =
                    analyticsApi.setAnalyticsOptOut(channel, detail.viewerUserId, optedOut)
            ) {
                is ApiResult.Ok -> null
                is ApiResult.Failure -> result.error.message
            }
        val refreshed: ViewerAnalyticsProfile? =
            if (error == null) {
                when (
                    val result: ApiResult<ViewerAnalyticsProfile> =
                        analyticsApi.viewerProfile(channel, detail.viewerUserId)
                ) {
                    is ApiResult.Ok -> result.value
                    is ApiResult.Failure -> detail.profile
                }
            } else {
                detail.profile
            }
        _viewerDetail.value = detail.copy(profile = refreshed, optOutBusy = false)
        return error
    }

    // Resolve (and cache) the active channel id, reused by the viewer drill-down. Null when none resolves.
    private suspend fun ensureChannel(): String? {
        channelId?.let { return it }
        return when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
            is ApiResult.Ok -> result.value.id.also { channelId = it }
            is ApiResult.Failure -> null
        }
    }

    private companion object {
        // The trailing window the page summarizes — well within the backend's 366-day cap.
        const val WINDOW_DAYS: Int = 30
        const val VIEWER_PAGE_SIZE: Int = 25
        const val NoChannelError: String = "No active channel — reconnect and try again."
    }
}

/** The viewer-list sort keys the backend `sort` query accepts. The screen shows role/metric names, not these. */
object ViewerSort {
    const val Watch: String = "Watch"
    const val Messages: String = "Messages"
    const val Commands: String = "Commands"
    const val Redemptions: String = "Redemptions"
    const val LastSeen: String = "LastSeen"

    /** Ordered for the sort selector. */
    val all: List<String> = listOf(Watch, Messages, Commands, Redemptions, LastSeen)
}

/** The viewer drill-down list render state. */
sealed interface ViewerListState {
    /** Not yet requested — the section shows a "load viewers" affordance rather than auto-fetching. */
    data object Idle : ViewerListState

    data object Loading : ViewerListState

    data class Ready(
        val viewers: List<ViewerProfileListEntry>,
        val search: String = "",
        val sort: String = ViewerSort.Watch,
        val page: Int = 1,
        val hasPrev: Boolean = false,
        val hasMore: Boolean = false,
        val total: Int? = null,
    ) : ViewerListState

    data class Error(val detail: String) : ViewerListState
}

/**
 * The open per-viewer drill-down. [loading] is true while the profile is being fetched; [error] is true when the
 * required profile failed to load. [streak] and [engagement] are supplementary and may be null/empty even on a
 * successful profile. [optOutBusy] guards the opt-out toggle while its write is in flight.
 */
data class ViewerDetailState(
    val viewerUserId: String,
    val displayName: String,
    val loading: Boolean = false,
    val profile: ViewerAnalyticsProfile? = null,
    val streak: WatchStreak? = null,
    val engagement: List<ViewerEngagementDay> = emptyList(),
    val error: Boolean = false,
    val optOutBusy: Boolean = false,
)

/** Today's UTC date as `yyyy-MM-dd` — the inclusive upper bound of the summary window. */
@OptIn(ExperimentalTime::class)
private fun today(): String = isoDate(Clock.System.now().epochSeconds.floorDiv(86_400))

/** The `yyyy-MM-dd` date [days] days before [date] (which is itself `yyyy-MM-dd`). */
private fun daysBefore(date: String, days: Int): String = isoDate(epochDay(date) - days)

/** Parse a `yyyy-MM-dd` string to its epoch day (days since 1970-01-01). */
private fun epochDay(date: String): Long {
    val parts: List<String> = date.split('-')
    return daysFromCivil(parts[0].toLong(), parts[1].toInt(), parts[2].toInt())
}

// Howard Hinnant's civil<->days algorithms (public domain) — a calendar date <-> epoch-day round trip with no
// datetime dependency, the one thing this page needs from a clock. Valid for the proleptic Gregorian calendar.

/** Days from the civil date (y, m, d) to 1970-01-01. */
private fun daysFromCivil(y: Long, m: Int, d: Int): Long {
    val yShifted: Long = if (m <= 2) y - 1 else y
    val era: Long = (if (yShifted >= 0) yShifted else yShifted - 399) / 400
    val yoe: Long = yShifted - era * 400
    val doy: Long = (153L * (if (m > 2) m - 3 else m + 9) + 2) / 5 + (d - 1)
    val doe: Long = yoe * 365 + yoe / 4 - yoe / 100 + doy
    return era * 146097 + doe - 719468
}

/** Render an epoch day (days since 1970-01-01) as a `yyyy-MM-dd` civil date. */
private fun isoDate(epochDay: Long): String {
    val z: Long = epochDay + 719468
    val era: Long = (if (z >= 0) z else z - 146096) / 146097
    val doe: Long = z - era * 146097
    val yoe: Long = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365
    val y: Long = yoe + era * 400
    val doy: Long = doe - (365 * yoe + yoe / 4 - yoe / 100)
    val mp: Long = (5 * doy + 2) / 153
    val d: Long = doy - (153 * mp + 2) / 5 + 1
    val m: Long = if (mp < 10) mp + 3 else mp - 9
    val year: Long = if (m <= 2) y + 1 else y
    return year.toString().padStart(4, '0') +
        "-" +
        m.toString().padStart(2, '0') +
        "-" +
        d.toString().padStart(2, '0')
}

/** The Analytics page render state. */
sealed interface AnalyticsState {
    data object Loading : AnalyticsState

    data class Ready(
        val summary: AnalyticsSummary,
        val daily: List<DailyMetricRow> = emptyList(),
        val topViewers: List<TopViewerEntry> = emptyList(),
        /** The channel's stream history for the per-stream picker (newest first); empty = no recorded streams. */
        val streams: List<StreamListItem> = emptyList(),
        /** The selected stream id, or null for the all-time view. */
        val selectedStreamId: String? = null,
        /** The selected stream's folded analytics — non-null only while a stream is selected and loaded. */
        val streamDetail: StreamAnalytics? = null,
        /** Non-null when the last [AnalyticsController.selectStream] detail fetch failed. */
        val streamError: String? = null,
    ) : AnalyticsState

    data class Error(val detail: String) : AnalyticsState
}

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
import kotlin.time.Clock
import kotlin.time.ExperimentalTime
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

    /** Resolve the active channel, then load its summary over the trailing window. */
    suspend fun load() {
        _state.value = AnalyticsState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = AnalyticsState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        val to: String = today()
        val from: String = daysBefore(to, WINDOW_DAYS - 1)

        when (
            val result: ApiResult<AnalyticsSummary> =
                analyticsApi.summary(channel.id, from = from, to = to)
        ) {
            is ApiResult.Failure -> _state.value = AnalyticsState.Error(result.error.message)
            is ApiResult.Ok -> _state.value = AnalyticsState.Ready(result.value)
        }
    }

    private companion object {
        // The trailing window the page summarizes — well within the backend's 366-day cap.
        const val WINDOW_DAYS: Int = 30
    }
}

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

    data class Ready(val summary: AnalyticsSummary) : AnalyticsState

    data class Error(val detail: String) : AnalyticsState
}

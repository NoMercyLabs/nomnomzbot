// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.time

import kotlinx.datetime.Instant
import kotlinx.datetime.LocalDate
import kotlinx.datetime.TimeZone
import kotlinx.datetime.toLocalDateTime

/**
 * Pure "how long ago" math shared by every "last ran / last fired" badge in the dashboard (timers,
 * TTS overlay, widgets, ...) — no network or Compose dependency, so it is tested directly against
 * fixed instants. Extracted once a third feature needed the exact same ISO-8601-parse-then-diff
 * logic (timers' `TimerSchedule`, TTS's `TtsOverlaySchedule` had each grown their own copy).
 */
object RelativeTime {
    /** Parses an ISO-8601 UTC instant, or null for a blank/malformed/absent value. */
    fun parseOrNull(value: String?): Instant? =
        value?.let { runCatching { Instant.parse(it) }.getOrNull() }

    /**
     * The same elapsed time, bucketed for display. Raw minutes are honest and unreadable past an
     * hour: "105790m ago" is months, and nobody reads it as that. The bucket carries a unit and a
     * number; the UI layer picks the translated wording, so no English lives here.
     *
     * Buckets roll up: seconds under a minute, minutes under an hour, hours under a day, days under
     * [DaysBeforeDate], then the calendar date in [zone].
     *
     * Clock skew (a timestamp in the future) collapses to [Elapsed.JustNow] rather than a negative
     * count, because a negative age is never the useful thing to show someone.
     */
    fun elapsedSince(iso: String?, now: Instant, zone: TimeZone = TimeZone.currentSystemDefault()): Elapsed? {
        val then: Instant = parseOrNull(iso) ?: return null
        val seconds: Long = (now - then).inWholeSeconds.coerceAtLeast(0)
        return when {
            seconds < JustNowSeconds -> Elapsed.JustNow
            seconds < SecondsPerMinute -> Elapsed.Seconds(seconds.toInt())
            seconds < SecondsPerHour -> Elapsed.Minutes((seconds / SecondsPerMinute).toInt())
            seconds < SecondsPerDay -> Elapsed.Hours((seconds / SecondsPerHour).toInt())
            seconds < SecondsPerDay * DaysBeforeDate -> Elapsed.Days((seconds / SecondsPerDay).toInt())
            else -> Elapsed.Date(then.toLocalDateTime(zone).date)
        }
    }

    private const val JustNowSeconds: Long = 10
    private const val SecondsPerMinute: Long = 60
    private const val SecondsPerHour: Long = 60 * 60
    private const val SecondsPerDay: Long = 60 * 60 * 24
    private const val DaysBeforeDate: Long = 30
}

/** How long ago something happened, at the coarseness a person reads it at. */
sealed interface Elapsed {
    data object JustNow : Elapsed

    data class Seconds(val value: Int) : Elapsed

    data class Minutes(val value: Int) : Elapsed

    data class Hours(val value: Int) : Elapsed

    data class Days(val value: Int) : Elapsed

    /** Too old for a count to read well: the calendar day it happened. */
    data class Date(val date: LocalDate) : Elapsed
}

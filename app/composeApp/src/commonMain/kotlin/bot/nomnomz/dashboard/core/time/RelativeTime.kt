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

    /** Whole minutes from [iso] until [now], or null when [iso] is null/unparseable (never happened yet). */
    fun minutesSince(iso: String?, now: Instant): Long? {
        val then: Instant = parseOrNull(iso) ?: return null
        return (now - then).inWholeMinutes
    }

    /**
     * The same elapsed time, bucketed for display. Raw minutes are honest and unreadable past an
     * hour — "1450m ago" is a day, and nobody reads it as one. The bucket carries a unit and a
     * number; the UI layer picks the translated wording, so no English lives here.
     *
     * Clock skew (a timestamp in the future) collapses to [Elapsed.JustNow] rather than a negative
     * count, because a negative age is never the useful thing to show someone.
     */
    fun elapsedSince(iso: String?, now: Instant): Elapsed? {
        val minutes: Long = minutesSince(iso, now) ?: return null
        val whole: Long = minutes.coerceAtLeast(0)
        return when {
            whole < 1 -> Elapsed.JustNow
            whole < MinutesPerHour -> Elapsed.Minutes(whole.toInt())
            whole < MinutesPerDay * 2 -> Elapsed.Hours((whole / MinutesPerHour).toInt())
            else -> Elapsed.Days((whole / MinutesPerDay).toInt())
        }
    }

    private const val MinutesPerHour: Long = 60
    private const val MinutesPerDay: Long = 60 * 24
}

/**
 * How long ago something happened, at the coarseness a person reads it at. Hours stay hours until
 * two days so "31 hours ago" is still available where it matters; past that, days.
 */
sealed interface Elapsed {
    data object JustNow : Elapsed

    data class Minutes(val value: Int) : Elapsed

    data class Hours(val value: Int) : Elapsed

    data class Days(val value: Int) : Elapsed
}

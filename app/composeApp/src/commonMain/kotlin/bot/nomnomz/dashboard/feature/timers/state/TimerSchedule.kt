// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.timers.state

import bot.nomnomz.dashboard.core.time.RelativeTime
import kotlin.time.Duration.Companion.minutes
import kotlinx.datetime.Instant

/**
 * Pure scheduling math for a timer's edit dialog — derived from the backend's persisted
 * `LastFiredAt` / `IntervalMinutes` / `NextMessageIndex` (the same fields [TimerService] on the
 * backend advances every tick; see `Commands/Jobs/TimerService.cs::ProcessTimerAsync`). No
 * network or Compose dependency, so it is tested directly against fixed instants.
 */
object TimerSchedule {

    /**
     * Common repeat cadences, in minutes, offered as quick-pick presets in the timer edit dialog. The
     * backend's floor (CreateTimerDto/UpdateTimerDto: `[Range(1, 1440)]` on `IntervalMinutes`) is whole
     * minutes, so this is the finest granularity a preset can offer — there is no sub-minute interval.
     */
    val IntervalPresetsMinutes: List<Int> = listOf(1, 5, 10, 15, 30, 60)

    /** Whether [presetMinutes] is the value currently entered in the (raw, editable) interval field. */
    fun isSelectedPreset(currentValue: String, presetMinutes: Int): Boolean =
        currentValue.toIntOrNull() == presetMinutes

    /** The interval-field value a click on the [presetMinutes] preset chip fills in. */
    fun presetFieldValue(presetMinutes: Int): String = presetMinutes.toString()

    /** The instant the timer next becomes eligible to fire, or null when it has never fired yet. */
    fun nextFireAt(lastFiredAt: String?, intervalMinutes: Int): Instant? {
        val last: Instant = RelativeTime.parseOrNull(lastFiredAt) ?: return null
        return last + intervalMinutes.minutes
    }

    /**
     * Whole minutes from [now] until the timer's next eligible fire — negative/zero once it is due.
     * Null when the timer has never fired (it is eligible as soon as it is next ticked).
     */
    fun minutesUntilNextFire(lastFiredAt: String?, intervalMinutes: Int, now: Instant): Long? {
        val next: Instant = nextFireAt(lastFiredAt, intervalMinutes) ?: return null
        return (next - now).inWholeMinutes
    }

    /** Whole minutes since the timer last fired, or null when it has never fired. */
    fun minutesSinceLastFire(lastFiredAt: String?, now: Instant): Long? =
        RelativeTime.minutesSince(lastFiredAt, now)

    /**
     * The 1-based rotation position (current message index, message count) — e.g. `2 of 5`. Null
     * when there is nothing to rotate through (zero or one message), matching [TimerService]'s
     * round-robin advance, which only wraps when `Messages.Count > 0`.
     */
    fun rotationPosition(nextMessageIndex: Int, messageCount: Int): Pair<Int, Int>? {
        if (messageCount <= 1) return null
        val position: Int = (nextMessageIndex % messageCount) + 1
        return position to messageCount
    }
}

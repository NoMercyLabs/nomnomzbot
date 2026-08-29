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

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.datetime.Instant

// Proves the pure scheduling math the timer edit dialog renders: the quick-pick interval presets fill in the
// exact value they advertise, and the last-fired / next-fire / rotation-position facts are computed correctly
// from a timer's persisted LastFiredAt/IntervalMinutes/NextMessageIndex state (the same fields
// TimerService.ProcessTimerAsync advances on the backend) — not merely "renders something".
class TimerScheduleTest {

    // --- Interval presets ---------------------------------------------------------------------------------

    @Test
    fun preset_list_offers_the_documented_minute_granular_cadences() {
        assertEquals(listOf(1, 5, 10, 15, 30, 60), TimerSchedule.IntervalPresetsMinutes)
    }

    @Test
    fun clicking_a_preset_fills_the_interval_field_with_exactly_that_minute_count() {
        for (minutes in TimerSchedule.IntervalPresetsMinutes) {
            assertEquals(minutes.toString(), TimerSchedule.presetFieldValue(minutes))
        }
    }

    @Test
    fun a_preset_is_marked_selected_only_when_the_field_holds_its_exact_value() {
        assertTrue(TimerSchedule.isSelectedPreset("30", presetMinutes = 30))
        assertTrue(TimerSchedule.isSelectedPreset(TimerSchedule.presetFieldValue(15), presetMinutes = 15))
        assertEquals(false, TimerSchedule.isSelectedPreset("30", presetMinutes = 60))
        // A custom, non-preset value (e.g. "45") selects none of the chips.
        assertEquals(false, TimerSchedule.isSelectedPreset("45", presetMinutes = 30))
        assertEquals(false, TimerSchedule.isSelectedPreset("", presetMinutes = 30))
    }

    // --- Last fired / next fire ---------------------------------------------------------------------------

    @Test
    fun a_timer_that_has_never_fired_has_no_next_fire_instant_or_countdown() {
        assertNull(TimerSchedule.nextFireAt(lastFiredAt = null, intervalMinutes = 30))
        assertNull(
            TimerSchedule.minutesUntilNextFire(
                lastFiredAt = null,
                intervalMinutes = 30,
                now = Instant.parse("2026-08-29T12:00:00Z"),
            )
        )
        assertNull(TimerSchedule.minutesSinceLastFire(lastFiredAt = null, now = Instant.parse("2026-08-29T12:00:00Z")))
    }

    @Test
    fun next_fire_is_last_fired_plus_the_interval() {
        val lastFired = "2026-08-29T12:00:00Z"
        val next: Instant? = TimerSchedule.nextFireAt(lastFiredAt = lastFired, intervalMinutes = 30)
        assertEquals(Instant.parse("2026-08-29T12:30:00Z"), next)
    }

    @Test
    fun a_timer_fired_ten_minutes_ago_on_a_thirty_minute_cadence_is_due_in_twenty() {
        val lastFired = "2026-08-29T12:00:00Z"
        val now = Instant.parse("2026-08-29T12:10:00Z")

        assertEquals(10L, TimerSchedule.minutesSinceLastFire(lastFired, now))
        assertEquals(20L, TimerSchedule.minutesUntilNextFire(lastFired, intervalMinutes = 30, now = now))
    }

    @Test
    fun a_timer_past_its_interval_reports_a_non_positive_countdown_instead_of_hiding_as_never_due() {
        val lastFired = "2026-08-29T12:00:00Z"
        // 40 minutes after a 30-minute-cadence fire: it has been due for 10 minutes.
        val now = Instant.parse("2026-08-29T12:40:00Z")

        val minutesLeft: Long? = TimerSchedule.minutesUntilNextFire(lastFired, intervalMinutes = 30, now = now)
        assertEquals(-10L, minutesLeft)
        assertTrue((minutesLeft ?: 0L) <= 0L)
    }

    // --- Rotation position ----------------------------------------------------------------------------------

    @Test
    fun a_single_message_timer_has_no_rotation_position_to_show() {
        assertNull(TimerSchedule.rotationPosition(nextMessageIndex = 0, messageCount = 0))
        assertNull(TimerSchedule.rotationPosition(nextMessageIndex = 0, messageCount = 1))
    }

    @Test
    fun rotation_position_is_the_one_based_next_message_index_out_of_the_total() {
        assertEquals(1 to 3, TimerSchedule.rotationPosition(nextMessageIndex = 0, messageCount = 3))
        assertEquals(2 to 3, TimerSchedule.rotationPosition(nextMessageIndex = 1, messageCount = 3))
        assertEquals(3 to 3, TimerSchedule.rotationPosition(nextMessageIndex = 2, messageCount = 3))
    }

    @Test
    fun rotation_position_wraps_the_same_way_the_backends_round_robin_advance_does() {
        // TimerService.ProcessTimerAsync: NextMessageIndex = (NextMessageIndex + 1) % Messages.Count — a
        // persisted index that is stale relative to a shrunk message list still resolves to a valid slot.
        assertEquals(1 to 3, TimerSchedule.rotationPosition(nextMessageIndex = 3, messageCount = 3))
    }
}

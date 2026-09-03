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

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlinx.datetime.Instant

// Proves the shared "how long ago" math every "last ran / last fired" badge in the dashboard depends on
// (timers, TTS overlay, widgets) — not merely "returns something".
class RelativeTimeTest {

    @Test
    fun parses_a_valid_iso_instant() {
        assertEquals(Instant.parse("2026-08-30T12:00:00Z"), RelativeTime.parseOrNull("2026-08-30T12:00:00Z"))
    }

    @Test
    fun null_and_malformed_values_parse_to_null() {
        assertNull(RelativeTime.parseOrNull(null))
        assertNull(RelativeTime.parseOrNull(""))
        assertNull(RelativeTime.parseOrNull("not-a-date"))
    }

    @Test
    fun minutes_since_computes_the_real_whole_minute_difference() {
        val then = "2026-08-30T12:00:00Z"
        val now = Instant.parse("2026-08-30T12:10:30Z")
        assertEquals(10L, RelativeTime.minutesSince(then, now))
    }

    @Test
    fun minutes_since_is_null_when_it_never_happened() {
        assertNull(RelativeTime.minutesSince(null, Instant.parse("2026-08-30T12:00:00Z")))
        assertNull(RelativeTime.minutesSince("garbage", Instant.parse("2026-08-30T12:00:00Z")))
    }

    // The bucket boundaries are the whole point of the type: the held-messages modal used to render a
    // day-old message as "1450m ago". Each of these asserts the side of a boundary it lands on.

    @Test
    fun under_a_minute_reads_as_just_now() {
        val now = Instant.parse("2026-08-30T12:00:30Z")
        assertEquals(Elapsed.JustNow, RelativeTime.elapsedSince("2026-08-30T12:00:00Z", now))
    }

    @Test
    fun minutes_hold_until_the_hour_then_become_hours() {
        val now = Instant.parse("2026-08-30T12:00:00Z")
        assertEquals(Elapsed.Minutes(59), RelativeTime.elapsedSince("2026-08-30T11:01:00Z", now))
        assertEquals(Elapsed.Hours(1), RelativeTime.elapsedSince("2026-08-30T11:00:00Z", now))
    }

    @Test
    fun hours_hold_until_two_days_then_become_days() {
        val now = Instant.parse("2026-09-01T12:00:00Z")
        // 47h — still worth reading as hours.
        assertEquals(Elapsed.Hours(47), RelativeTime.elapsedSince("2026-08-30T13:00:00Z", now))
        // 48h — the boundary flips to days.
        assertEquals(Elapsed.Days(2), RelativeTime.elapsedSince("2026-08-30T12:00:00Z", now))
    }

    @Test
    fun the_1450_minute_case_reads_as_hours_not_raw_minutes() {
        // The exact case from the Held-messages modal: 1450 minutes rendered as "1450m ago".
        // Inside the 48h window it stays hours, which is the precise AND readable answer.
        val now = Instant.parse("2026-08-31T12:10:00Z")
        assertEquals(Elapsed.Hours(24), RelativeTime.elapsedSince("2026-08-30T12:00:00Z", now))
    }

    @Test
    fun a_future_timestamp_collapses_to_just_now_rather_than_a_negative_age() {
        val now = Instant.parse("2026-08-30T12:00:00Z")
        assertEquals(Elapsed.JustNow, RelativeTime.elapsedSince("2026-08-30T12:30:00Z", now))
    }

    @Test
    fun elapsed_is_null_when_it_never_happened() {
        assertNull(RelativeTime.elapsedSince(null, Instant.parse("2026-08-30T12:00:00Z")))
        assertNull(RelativeTime.elapsedSince("garbage", Instant.parse("2026-08-30T12:00:00Z")))
    }
}

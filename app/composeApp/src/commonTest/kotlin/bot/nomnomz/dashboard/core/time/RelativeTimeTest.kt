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
import kotlin.time.Duration.Companion.minutes
import kotlinx.datetime.Instant
import kotlinx.datetime.LocalDate
import kotlinx.datetime.TimeZone
import kotlinx.datetime.toLocalDateTime

// Proves the shared "how long ago" math every "last ran / last fired" badge in the dashboard depends on
// (timers, TTS overlay, widgets, attention inbox) — not merely "returns something".
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

    // The buckets roll up seconds -> minutes -> hours -> days -> a date. A live inbox row read "105790m ago"
    // (about 73 days) because raw minutes were rendered; each test asserts the side of a boundary it lands on.

    private val utc: TimeZone = TimeZone.UTC

    private fun elapsed(then: String, now: String): Elapsed? =
        RelativeTime.elapsedSince(then, Instant.parse(now), utc)

    @Test
    fun under_ten_seconds_reads_as_just_now() {
        assertEquals(Elapsed.JustNow, elapsed("2026-08-30T12:00:00Z", "2026-08-30T12:00:09Z"))
    }

    @Test
    fun seconds_hold_until_the_minute_then_become_minutes() {
        assertEquals(Elapsed.Seconds(10), elapsed("2026-08-30T12:00:00Z", "2026-08-30T12:00:10Z"))
        assertEquals(Elapsed.Seconds(59), elapsed("2026-08-30T12:00:00Z", "2026-08-30T12:00:59Z"))
        assertEquals(Elapsed.Minutes(1), elapsed("2026-08-30T12:00:00Z", "2026-08-30T12:01:00Z"))
    }

    @Test
    fun minutes_hold_until_the_hour_then_become_hours() {
        assertEquals(Elapsed.Minutes(59), elapsed("2026-08-30T11:01:00Z", "2026-08-30T12:00:00Z"))
        assertEquals(Elapsed.Hours(1), elapsed("2026-08-30T11:00:00Z", "2026-08-30T12:00:00Z"))
    }

    @Test
    fun hours_hold_until_the_day_then_become_days() {
        assertEquals(Elapsed.Hours(23), elapsed("2026-08-29T13:00:00Z", "2026-08-30T12:00:00Z"))
        assertEquals(Elapsed.Days(1), elapsed("2026-08-29T12:00:00Z", "2026-08-30T12:00:00Z"))
    }

    @Test
    fun days_hold_until_thirty_then_become_a_date() {
        assertEquals(Elapsed.Days(29), elapsed("2026-08-01T12:00:00Z", "2026-08-30T12:00:00Z"))
        assertEquals(
            Elapsed.Date(LocalDate(2026, 7, 31)),
            elapsed("2026-07-31T12:00:00Z", "2026-08-30T12:00:00Z"),
        )
    }

    @Test
    fun the_105790_minute_case_reads_as_a_date_not_raw_minutes() {
        // The exact value seen live in the attention popover: 105790 minutes is about 73 days.
        val now: Instant = Instant.parse("2026-09-30T12:00:00Z")
        val then: Instant = now - 105790.minutes
        val result: Elapsed? = RelativeTime.elapsedSince(then.toString(), now, utc)
        assertEquals(Elapsed.Date(then.toLocalDateTime(utc).date), result)
    }

    @Test
    fun the_date_is_the_calendar_day_in_the_given_zone() {
        // 23:30 UTC on the 1st is already the 2nd in Amsterdam (UTC+2 in summer).
        val amsterdam: TimeZone = TimeZone.of("Europe/Amsterdam")
        val result: Elapsed? =
            RelativeTime.elapsedSince("2026-06-01T23:30:00Z", Instant.parse("2026-09-30T12:00:00Z"), amsterdam)
        assertEquals(Elapsed.Date(LocalDate(2026, 6, 2)), result)
    }

    @Test
    fun a_future_timestamp_collapses_to_just_now_rather_than_a_negative_age() {
        assertEquals(Elapsed.JustNow, elapsed("2026-08-30T12:30:00Z", "2026-08-30T12:00:00Z"))
    }

    @Test
    fun elapsed_is_null_when_it_never_happened() {
        assertNull(RelativeTime.elapsedSince(null, Instant.parse("2026-08-30T12:00:00Z"), utc))
        assertNull(RelativeTime.elapsedSince("garbage", Instant.parse("2026-08-30T12:00:00Z"), utc))
    }
}

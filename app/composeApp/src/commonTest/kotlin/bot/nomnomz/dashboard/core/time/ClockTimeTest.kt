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
import kotlinx.datetime.TimeZone

// The Spotify-block inbox item says until when music is paused. The server sends UTC; the streamer reads it
// on their own clock.
class ClockTimeTest {
    private val amsterdam: TimeZone = TimeZone.of("Europe/Amsterdam")
    private val now: Instant = Instant.parse("2026-10-02T14:09:00Z")

    @Test
    fun a_moment_later_today_reads_as_the_local_time_only() {
        assertEquals("22:47", ClockTime.of("2026-10-02T20:47:03.0000000+00:00", now, amsterdam))
    }

    @Test
    fun a_moment_on_another_local_day_carries_its_date() {
        assertEquals("2026-10-03 01:30", ClockTime.of("2026-10-02T23:30:00Z", now, amsterdam))
    }

    @Test
    fun a_missing_or_malformed_value_reads_as_nothing() {
        assertNull(ClockTime.of("", now, amsterdam))
        assertNull(ClockTime.of("not a time", now, amsterdam))
    }
}

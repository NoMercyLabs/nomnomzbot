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
}

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
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

class ScheduleTimesTest {
    @Test
    fun `a same-day segment shows its start and end in the saved zone`() {
        // 18:00Z..20:30Z on 2026-10-05 is 20:00..22:30 in Amsterdam (CEST, UTC+2).
        assertEquals(
            "2026-10-05 20:00 → 22:30 (Europe/Amsterdam)",
            ScheduleTimes.range("2026-10-05T18:00:00Z", "2026-10-05T20:30:00Z", "Europe/Amsterdam"),
        )
    }

    @Test
    fun `a segment that crosses midnight in the zone carries both dates`() {
        // 22:30Z..23:30Z is 00:30..01:30 next day in Amsterdam, but 22:30..23:30 in UTC: same day there.
        assertEquals(
            "2026-10-06 00:30 → 01:30 (Europe/Amsterdam)",
            ScheduleTimes.range("2026-10-05T22:30:00Z", "2026-10-05T23:30:00Z", "Europe/Amsterdam"),
        )
        assertEquals(
            "2026-10-05 23:00 → 2026-10-06 01:00 (UTC)",
            ScheduleTimes.range("2026-10-05T23:00:00Z", "2026-10-06T01:00:00Z", "UTC"),
        )
    }

    @Test
    fun `the zone shifts the wall clock for the same instant`() {
        assertEquals(
            "2026-10-05 14:00 → 16:30 (America/New_York)",
            ScheduleTimes.range("2026-10-05T18:00:00Z", "2026-10-05T20:30:00Z", "America/New_York"),
        )
    }

    @Test
    fun `no saved zone falls back to the device zone and names it`() {
        val device: String = ScheduleTimes.deviceZoneId()
        val text: String = ScheduleTimes.range("2026-10-05T18:00:00Z", "2026-10-05T20:30:00Z", null)
        assertTrue(text.endsWith("($device)"), text)
    }

    @Test
    fun `an unknown saved zone falls back to the device zone instead of crashing`() {
        val device: String = ScheduleTimes.deviceZoneId()
        val text: String =
            ScheduleTimes.range("2026-10-05T18:00:00Z", "2026-10-05T20:30:00Z", "Mars/Olympus")
        assertTrue(text.endsWith("($device)"), text)
    }

    @Test
    fun `a malformed instant is shown raw so the row never goes blank`() {
        assertEquals("nope → also-nope", ScheduleTimes.range("nope", "also-nope", "UTC"))
    }

    @Test
    fun `zone validity accepts real ids and refuses typos and blanks`() {
        assertTrue(ScheduleTimes.isValidZone("Europe/Amsterdam"))
        assertTrue(ScheduleTimes.isValidZone("UTC"))
        assertFalse(ScheduleTimes.isValidZone("Mars/Olympus"))
        assertFalse(ScheduleTimes.isValidZone("Amsterdam"))
        assertFalse(ScheduleTimes.isValidZone(""))
        assertFalse(ScheduleTimes.isValidZone(null))
    }

    @Test
    fun `the zone list is sorted and filters by a case-insensitive fragment`() {
        val all: List<String> = ScheduleTimes.zoneIds()
        assertEquals(all.sorted(), all)
        assertTrue("Europe/Amsterdam" in all)
        val hits: List<String> = ScheduleTimes.matchingZones("amsterd")
        assertTrue(hits.contains("Europe/Amsterdam"))
        assertTrue(hits.all { it.contains("amsterd", ignoreCase = true) })
    }

    @Test
    fun `the default zone is the saved one when valid, else the device zone`() {
        assertEquals("Europe/Amsterdam", ScheduleTimes.defaultZone("Europe/Amsterdam"))
        assertEquals(ScheduleTimes.deviceZoneId(), ScheduleTimes.defaultZone(null))
        assertEquals(ScheduleTimes.deviceZoneId(), ScheduleTimes.defaultZone("garbage"))
        assertNull(ScheduleTimes.savedZoneOrNull("garbage"))
    }
}

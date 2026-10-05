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
import kotlinx.datetime.TimeZone
import kotlinx.datetime.offsetAt
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/**
 * Runs on every target. On wasmJs kotlinx-datetime has no IANA database of its own, so this proves the bundled
 * one is loaded (the deployed picker showed "No matching timezones" without it).
 */
class ZoneDatabaseTest {
    @Test
    fun `the zone list holds real IANA zones and the picker lists them for an empty query`() {
        assertTrue("Europe/Amsterdam" in TimeZone.availableZoneIds)
        assertTrue("Europe/Amsterdam" in ScheduleTimes.matchingZones(""))
        assertEquals(listOf("Europe/Amsterdam"), ScheduleTimes.matchingZones("amsterdam"))
    }

    @Test
    fun `a zone id round-trips and applies its daylight saving offset`() {
        val zone: TimeZone = TimeZone.of("Europe/Amsterdam")
        assertEquals("Europe/Amsterdam", zone.id)
        assertEquals(2 * 3600, zone.offsetAt(Instant.parse("2026-07-01T12:00:00Z")).totalSeconds)
        assertEquals(1 * 3600, zone.offsetAt(Instant.parse("2026-01-01T12:00:00Z")).totalSeconds)
        assertTrue(ScheduleTimes.isValidZone("Europe/Amsterdam"))
    }
}

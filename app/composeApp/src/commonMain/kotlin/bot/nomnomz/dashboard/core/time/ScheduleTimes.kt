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

import kotlinx.datetime.LocalDateTime
import kotlinx.datetime.TimeZone
import kotlinx.datetime.toLocalDateTime

/**
 * Zone-aware schedule time text and the IANA zone list the zone picker offers. Pure functions, so the row text
 * for a known instant and zone is testable without a screen.
 */
object ScheduleTimes {
    /** The device's own zone id, the default when the streamer has no valid saved zone. */
    fun deviceZoneId(): String = TimeZone.currentSystemDefault().id

    /** True when [zone] is a non-blank id the platform knows. */
    fun isValidZone(zone: String?): Boolean = !zone.isNullOrBlank() && zone in zoneIdSet

    /** [zone] when it is a valid saved zone, else null. */
    fun savedZoneOrNull(zone: String?): String? = zone?.takeIf { isValidZone(it) }

    /** The zone a picker starts on: the saved zone when valid, else the device zone. */
    fun defaultZone(saved: String?): String = savedZoneOrNull(saved) ?: deviceZoneId()

    /** Every known zone id, sorted. */
    fun zoneIds(): List<String> = zoneIdsSorted

    /** The zone ids containing [query] (case-insensitive); an empty query lists all of them. */
    fun matchingZones(query: String): List<String> {
        val needle: String = query.trim()
        return if (needle.isEmpty()) zoneIdsSorted else zoneIdsSorted.filter { it.contains(needle, ignoreCase = true) }
    }

    /**
     * "2026-10-05 20:00 → 22:30 (Europe/Amsterdam)": start and end as wall-clock time in [zone] (the device zone
     * when [zone] is missing or unknown). The end carries its date only when it falls on another day. An
     * unparseable instant is shown raw, so a row never goes blank.
     */
    fun range(startIso: String, endIso: String, zone: String?): String {
        val zoneId: String = defaultZone(zone)
        val tz: TimeZone = TimeZone.of(zoneId)
        val start: LocalDateTime? = RelativeTime.parseOrNull(startIso)?.toLocalDateTime(tz)
        val end: LocalDateTime? = RelativeTime.parseOrNull(endIso)?.toLocalDateTime(tz)
        if (start == null || end == null) return "$startIso → $endIso"
        val endText: String = if (end.date == start.date) clock(end) else "${end.date} ${clock(end)}"
        return "${start.date} ${clock(start)} → $endText ($zoneId)"
    }

    private fun clock(at: LocalDateTime): String =
        "${at.hour.toString().padStart(2, '0')}:${at.minute.toString().padStart(2, '0')}"

    private val zoneIdSet: Set<String> by lazy { TimeZone.availableZoneIds }
    private val zoneIdsSorted: List<String> by lazy { zoneIdSet.sorted() }
}

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
import kotlinx.datetime.LocalDateTime
import kotlinx.datetime.TimeZone
import kotlinx.datetime.toLocalDateTime

/**
 * A future moment as a wall-clock time in the viewer's zone ("22:47"), for "until …" text. A moment on
 * another day than [now] also carries its date ("2026-10-03 22:47"), so the time is never read as today.
 */
object ClockTime {
    /** The formatted time, or null for a blank/malformed/absent value. */
    fun of(iso: String?, now: Instant, zone: TimeZone = TimeZone.currentSystemDefault()): String? {
        val at: LocalDateTime = RelativeTime.parseOrNull(iso)?.toLocalDateTime(zone) ?: return null
        val time: String = "${pad(at.hour)}:${pad(at.minute)}"
        return if (at.date == now.toLocalDateTime(zone).date) time else "${at.date} $time"
    }

    private fun pad(value: Int): String = value.toString().padStart(2, '0')
}

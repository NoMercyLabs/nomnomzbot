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

/**
 * Pure "how long ago" math shared by every "last ran / last fired" badge in the dashboard (timers,
 * TTS overlay, widgets, ...) — no network or Compose dependency, so it is tested directly against
 * fixed instants. Extracted once a third feature needed the exact same ISO-8601-parse-then-diff
 * logic (timers' `TimerSchedule`, TTS's `TtsOverlaySchedule` had each grown their own copy).
 */
object RelativeTime {
    /** Parses an ISO-8601 UTC instant, or null for a blank/malformed/absent value. */
    fun parseOrNull(value: String?): Instant? =
        value?.let { runCatching { Instant.parse(it) }.getOrNull() }

    /** Whole minutes from [iso] until [now], or null when [iso] is null/unparseable (never happened yet). */
    fun minutesSince(iso: String?, now: Instant): Long? {
        val then: Instant = parseOrNull(iso) ?: return null
        return (now - then).inWholeMinutes
    }
}

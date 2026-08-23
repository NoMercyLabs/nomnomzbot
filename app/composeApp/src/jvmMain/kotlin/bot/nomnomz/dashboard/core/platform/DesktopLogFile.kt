// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.platform

import java.io.File
import kotlinx.datetime.Clock
import kotlinx.datetime.TimeZone
import kotlinx.datetime.toLocalDateTime

/**
 * Desktop rolling log file (S111c) — a single capped-size text file under [DesktopDataDir], since
 * a windowless launch (a double-clicked EXE/app bundle, no console) otherwise leaves the operator
 * with zero diagnostics when something goes wrong before the dashboard renders. Lives at
 * `<data dir>/logs/app.log` — documented in DEPLOY.md's desktop section.
 *
 * Rotation is a simple size cap, not a rolling series of numbered files: once `app.log` would
 * exceed [maxBytes], the oldest half of its lines is dropped before the new line is appended. That
 * keeps the file small and dependency-free (no logging framework) at the cost of losing old lines
 * rather than archiving them — acceptable for a desktop diagnostics tail, not an audit log.
 *
 * Callers MUST NOT pass token/secret values into [append] — this class does no redaction of its
 * own, the same "never log a token" rule the codebase already applies everywhere else.
 */
class DesktopLogFile internal constructor(private val file: File, private val maxBytes: Long) {

    constructor(maxBytes: Long = DEFAULT_MAX_BYTES) : this(File(DesktopDataDir.resolve(), "logs/app.log"), maxBytes)

    fun append(tag: String, message: String) {
        runCatching {
            file.parentFile?.mkdirs()
            val timestamp: String =
                Clock.System.now().toLocalDateTime(TimeZone.currentSystemDefault()).toString()
            val line = "$timestamp [$tag] $message${System.lineSeparator()}"

            rotateIfOverCap(additional = line.toByteArray(Charsets.UTF_8).size.toLong())
            file.appendText(line)
        }
    }

    fun readAll(): String = runCatching { file.readText() }.getOrDefault("")

    private fun rotateIfOverCap(additional: Long) {
        if (!file.exists()) return
        if (file.length() + additional <= maxBytes) return

        val lines: List<String> = runCatching { file.readLines() }.getOrDefault(emptyList())
        val kept: List<String> = lines.drop(lines.size / 2)
        file.writeText(if (kept.isEmpty()) "" else kept.joinToString(System.lineSeparator()) + System.lineSeparator())
    }

    companion object {
        const val DEFAULT_MAX_BYTES: Long = 2L * 1024 * 1024
    }
}

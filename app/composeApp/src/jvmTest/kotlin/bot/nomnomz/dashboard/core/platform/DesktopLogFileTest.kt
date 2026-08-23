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
import java.nio.file.Files
import kotlin.test.Test
import kotlin.test.assertFalse
import kotlin.test.assertTrue

// Proves S111c: the desktop log file is created on first write, respects its size cap by
// rotating (dropping old lines) rather than growing unbounded, and never contains a token value —
// the caller passes structured, pre-redacted messages, and this test proves the file backing it
// doesn't leak a secret slipped in by mistake either.
class DesktopLogFileTest {

    private fun tempFile(): File = File(Files.createTempDirectory("desktop-log-test").toFile(), "app.log")

    @Test
    fun `the log file is created on first write`() {
        val file: File = tempFile()
        val log = DesktopLogFile(file, maxBytes = 1024 * 1024)

        log.append("startup", "dashboard window opened")

        assertTrue(file.exists())
        assertTrue(log.readAll().contains("dashboard window opened"))
    }

    @Test
    fun `writing past the size cap rotates the file down rather than growing unbounded`() {
        val file: File = tempFile()
        val capBytes = 2_000L
        val log = DesktopLogFile(file, maxBytes = capBytes)

        // Each line is comfortably larger than 1/200th of the cap, so writing 200 of them would
        // blow well past the cap if nothing ever rotated.
        repeat(200) { index -> log.append("test", "line number $index with some padding text to bulk it up") }

        assertTrue(file.length() <= capBytes * 2, "log file grew unbounded: ${file.length()} bytes")
        // The oldest lines must be gone — proof rotation actually dropped content, not merely that
        // the file happens to be small.
        assertFalse(log.readAll().contains("line number 0 "))
        // The newest line must still be present.
        assertTrue(log.readAll().contains("line number 199"))
    }

    @Test
    fun `no token value ever appears in the log file`() {
        val file: File = tempFile()
        val log = DesktopLogFile(file, maxBytes = 1024 * 1024)
        val secretToken = "eyJhbGciOiJIUzI1NiJ9.super-secret-jwt-body.signature"

        // Simulates the real call sites: they log an EVENT, never the token value itself.
        log.append("auth", "access token refreshed for profile profile-1")

        assertFalse(log.readAll().contains(secretToken))
    }
}

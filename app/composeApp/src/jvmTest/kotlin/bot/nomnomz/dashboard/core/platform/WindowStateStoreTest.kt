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
import kotlin.test.assertEquals

// Proves S111c: window geometry (size/position/maximized) round-trips through disk across a
// restart of the store, a corrupt file yields sane defaults instead of crashing, and a saved
// position that no longer maps to any connected monitor is discarded rather than opening the
// window somewhere unreachable.
class WindowStateStoreTest {

    private val default = WindowGeometry(x = 100f, y = 100f, width = 1320f, height = 920f, maximized = false)

    private fun tempFile(): File = File(Files.createTempDirectory("window-state-test").toFile(), "window-state.json")

    @Test
    fun `a saved geometry survives restarting the store from disk`() {
        val file: File = tempFile()
        val saved = WindowGeometry(x = 42f, y = 17f, width = 1600f, height = 1000f, maximized = false)

        WindowStateStore(file, default).save(saved)
        val restarted = WindowStateStore(file, default)

        assertEquals(saved, restarted.load())
    }

    @Test
    fun `a missing file yields the default geometry`() {
        val file: File = tempFile()

        assertEquals(default, WindowStateStore(file, default).load())
    }

    @Test
    fun `a corrupt file yields the default geometry instead of crashing`() {
        val file: File = tempFile()
        file.parentFile.mkdirs()
        file.writeText("{ not valid json at all")

        assertEquals(default, WindowStateStore(file, default).load())
    }

    @Test
    fun `a maximized window is restored regardless of its stale position`() {
        val file: File = tempFile()
        val saved = WindowGeometry(x = -5000f, y = -5000f, width = 1320f, height = 920f, maximized = true)
        WindowStateStore(file, default).save(saved)
        val store = WindowStateStore(file, default)

        val screens = listOf(ScreenBounds(0f, 0f, 1920f, 1080f))
        assertEquals(saved, store.loadSanitized(screens))
    }

    @Test
    fun `a position off every connected monitor falls back to the default`() {
        val file: File = tempFile()
        val offScreen = WindowGeometry(x = 9000f, y = 9000f, width = 1320f, height = 920f, maximized = false)
        WindowStateStore(file, default).save(offScreen)
        val store = WindowStateStore(file, default)

        val screens = listOf(ScreenBounds(0f, 0f, 1920f, 1080f))
        assertEquals(default, store.loadSanitized(screens))
    }

    @Test
    fun `a position that overlaps a connected monitor is kept`() {
        val file: File = tempFile()
        val onScreen = WindowGeometry(x = 200f, y = 200f, width = 1320f, height = 920f, maximized = false)
        WindowStateStore(file, default).save(onScreen)
        val store = WindowStateStore(file, default)

        val screens = listOf(ScreenBounds(0f, 0f, 1920f, 1080f))
        assertEquals(onScreen, store.loadSanitized(screens))
    }
}

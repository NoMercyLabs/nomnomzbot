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
import kotlin.test.assertFalse
import kotlin.test.assertTrue

// Proves S111c: the desktop data directory resolves to the platform-proper location per OS — most
// importantly macOS, which every store used to get wrong (falling through to the Linux/XDG path) —
// and that a previously-wrong macOS directory is migrated rather than silently orphaned.
class DesktopDataDirTest {

    private fun tempHome(): File = Files.createTempDirectory("desktop-data-dir-test").toFile()

    @Test
    fun `macOS resolves to Library Application Support`() {
        val home: File = tempHome()

        val resolved: File =
            DesktopDataDir.resolveBaseDir("NomNomzBot", osName = "Mac OS X", userHome = home.path) { null }

        assertEquals(File(home, "Library/Application Support/NomNomzBot").path, resolved.path)
    }

    @Test
    fun `Windows resolves to LOCALAPPDATA when set`() {
        val home: File = tempHome()
        val localAppData = File(home, "AppData/Local")

        val resolved: File =
            DesktopDataDir.resolveBaseDir("NomNomzBot", osName = "Windows 11", userHome = home.path) { key ->
                if (key == "LOCALAPPDATA") localAppData.path else null
            }

        assertEquals(File(localAppData, "NomNomzBot").path, resolved.path)
    }

    @Test
    fun `Windows falls back to AppData Local under the home dir when LOCALAPPDATA is unset`() {
        val home: File = tempHome()

        val resolved: File =
            DesktopDataDir.resolveBaseDir("NomNomzBot", osName = "Windows 11", userHome = home.path) { null }

        assertEquals(File(home, "AppData/Local/NomNomzBot").path, resolved.path)
    }

    @Test
    fun `Linux resolves to XDG_DATA_HOME when set, else dotlocal-share`() {
        val home: File = tempHome()
        val xdg = File(home, "custom-xdg")

        val withXdg: File =
            DesktopDataDir.resolveBaseDir("NomNomzBot", osName = "Linux", userHome = home.path) { key ->
                if (key == "XDG_DATA_HOME") xdg.path else null
            }
        assertEquals(File(xdg, "NomNomzBot").path, withXdg.path)

        val withoutXdg: File =
            DesktopDataDir.resolveBaseDir("NomNomzBot", osName = "Linux", userHome = home.path) { null }
        assertEquals(File(home, ".local/share/NomNomzBot").path, withoutXdg.path)
    }

    @Test
    fun `migration moves files from the old location into the new one and removes the old dir`() {
        val home: File = tempHome()
        val oldDir = File(home, "old-location").apply { mkdirs() }
        File(oldDir, "tokens.json").writeText("some-persisted-state")
        val newDir = File(home, "new-location")

        DesktopDataDir.migrateIfNeeded(oldDir, newDir)

        assertTrue(File(newDir, "tokens.json").exists())
        assertEquals("some-persisted-state", File(newDir, "tokens.json").readText())
        assertFalse(oldDir.exists())
    }

    @Test
    fun `migration is a no-op when the new location already has data`() {
        val home: File = tempHome()
        val oldDir = File(home, "old-location").apply { mkdirs() }
        File(oldDir, "tokens.json").writeText("stale-data")
        val newDir = File(home, "new-location").apply { mkdirs() }
        File(newDir, "tokens.json").writeText("current-data")

        DesktopDataDir.migrateIfNeeded(oldDir, newDir)

        assertEquals("current-data", File(newDir, "tokens.json").readText())
        assertTrue(oldDir.exists(), "old dir must be left alone once the new dir already holds data")
    }

    @Test
    fun `migration is a no-op when there is nothing to migrate`() {
        val home: File = tempHome()
        val oldDir = File(home, "never-existed")
        val newDir = File(home, "new-location")

        DesktopDataDir.migrateIfNeeded(oldDir, newDir)

        assertFalse(newDir.exists())
    }
}

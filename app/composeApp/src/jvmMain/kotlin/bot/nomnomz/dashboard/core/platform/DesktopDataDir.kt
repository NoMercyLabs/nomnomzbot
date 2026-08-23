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

// Single source of truth for "where does the desktop app's per-user data live" (S111c) — every
// vault/store under jvmMain (TokenVault, ActiveProfileVault, ActiveChannelVault,
// FileSavedConnectionsStore, the language/emoji preference stores, window state, and the log
// file) resolves its base directory through here instead of re-deriving it.
//
// Before this slice every one of those stores used `LOCALAPPDATA ?: XDG_DATA_HOME ?: ~/.local/share`,
// which is correct on Windows and Linux but WRONG on macOS — `~/.local/share` is a Linux/XDG
// convention macOS apps don't use or scan. The platform-proper location is
// `~/Library/Application Support/<app>`. [resolve] fixes that and transparently migrates any
// files a previous build already wrote to the wrong (XDG-style) location on a Mac.
object DesktopDataDir {

    /** Pure OS→base-directory mapping, parameterized for testability (no live System.* reads). */
    fun resolveBaseDir(appName: String, osName: String, userHome: String, env: (String) -> String?): File {
        val lowerOs: String = osName.lowercase()
        val base: File =
            when {
                lowerOs.contains("mac") || lowerOs.contains("darwin") ->
                    File(userHome, "Library${File.separator}Application Support")
                lowerOs.contains("win") ->
                    File(env("LOCALAPPDATA") ?: File(userHome, "AppData${File.separator}Local").path)
                else ->
                    File(env("XDG_DATA_HOME") ?: File(userHome, ".local${File.separator}share").path)
            }
        return File(base, appName)
    }

    /** The macOS-only legacy (pre-fix) location — the XDG path a Mac build mistakenly used. */
    fun legacyMacDir(appName: String, userHome: String): File =
        File(File(userHome, ".local${File.separator}share"), appName)

    /**
     * Best-effort, non-destructive migration: if [oldDir] holds data and [newDir] is empty/missing,
     * copy [oldDir]'s contents into [newDir] and remove [oldDir]. A partial failure (e.g. a locked
     * file) leaves [oldDir] in place rather than losing data — the next launch retries.
     */
    fun migrateIfNeeded(oldDir: File, newDir: File) {
        if (oldDir.canonicalPath == newDir.canonicalPath) return
        if (!oldDir.exists() || !oldDir.isDirectory) return
        if (newDir.exists() && !newDir.listFiles().isNullOrEmpty()) return

        newDir.mkdirs()
        val allCopied: Boolean =
            oldDir.listFiles()?.all { source: File ->
                runCatching { source.copyRecursively(File(newDir, source.name), overwrite = false) }.isSuccess
            } ?: true

        if (allCopied) {
            oldDir.deleteRecursively()
        }
    }

    /** Resolves (and creates) the real base directory for the running JVM, migrating legacy macOS data first. */
    fun resolve(appName: String = "NomNomzBot"): File {
        val osName: String = System.getProperty("os.name", "")
        val userHome: String = System.getProperty("user.home", "")
        val newDir: File = resolveBaseDir(appName, osName, userHome, System::getenv)

        if (osName.lowercase().contains("mac")) {
            migrateIfNeeded(legacyMacDir(appName, userHome), newDir)
        }

        newDir.mkdirs()
        return newDir
    }
}

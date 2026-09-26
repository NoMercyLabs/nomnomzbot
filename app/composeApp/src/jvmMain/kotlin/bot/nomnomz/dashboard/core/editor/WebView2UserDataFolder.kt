// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.editor

import bot.nomnomz.dashboard.core.platform.DesktopDataDir
import com.sun.jna.Library
import com.sun.jna.Native
import com.sun.jna.WString
import java.io.File

// WebView2 keeps its profile (cache, cookies, the editor theme choice) in a "user data folder". The binding
// creates its environment without naming one, so the runtime falls back to `<exe dir>\<exe name>.WebView2` —
// beside java.exe, which lives under Program Files and is read-only: environment creation then fails with
// E_ACCESSDENIED and the window stays blank. The runtime's documented override is the process environment
// variable WEBVIEW2_USER_DATA_FOLDER, read when the environment is created; Java cannot set its own process
// environment, so this sets it through kernel32 before the first web view exists.
internal object WebView2UserDataFolder {
    const val VARIABLE: String = "WEBVIEW2_USER_DATA_FOLDER"

    private interface Kernel32 : Library {
        @Suppress("FunctionName")
        fun SetEnvironmentVariableW(name: WString, value: WString): Boolean
    }

    /** The folder this app gives WebView2: its own app-data dir, never a system-wide location. */
    fun folder(appDataDir: File = DesktopDataDir.resolve()): File = File(appDataDir, "webview2")

    /**
     * Points WebView2 at [folder] unless the operator already chose one. True when the variable is in place,
     * false when it could not be set (the caller then reports the web view as unavailable instead of hanging).
     */
    fun ensure(): Boolean {
        if (!System.getenv(VARIABLE).isNullOrBlank()) return true
        val target: File = folder().apply { mkdirs() }
        return runCatching {
                val kernel32: Kernel32 = Native.load("kernel32", Kernel32::class.java)
                kernel32.SetEnvironmentVariableW(WString(VARIABLE), WString(target.absolutePath))
            }
            .getOrDefault(false)
    }
}

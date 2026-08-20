// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.io

import java.io.File

private val chromiumCandidates: List<String> =
    listOf(
        "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
        "C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe",
        "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
        "/usr/bin/google-chrome",
        "/usr/bin/chromium-browser",
        "/usr/bin/microsoft-edge",
        "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
        "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
    )

private fun findChromiumExecutable(): String? = chromiumCandidates.firstOrNull { File(it).exists() }

actual fun captureWindowSupported(): Boolean = findChromiumExecutable() != null

actual fun openCaptureWindow(url: String, width: Int, height: Int): Boolean {
    val executable: String = findChromiumExecutable() ?: return false
    return try {
        ProcessBuilder(
                executable,
                "--app=$url",
                "--window-size=$width,$height",
                "--new-window",
            )
            .start()
        true
    } catch (_: Exception) {
        false
    }
}

// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

@file:OptIn(ExperimentalWasmJsInterop::class)

package bot.nomnomz.dashboard.core.io

import kotlin.js.ExperimentalWasmJsInterop

// The web dashboard cannot spawn a native process, but `window.open` with explicit size features
// opens a genuine, separate OS-level browser window (no tab strip) — OBS can Window/Application-Audio
// Capture that the same way as a native app window. A user gesture is always present here (the call
// originates from an onClick), which `window.open` popup blockers require.
actual fun captureWindowSupported(): Boolean = true

actual fun openCaptureWindow(url: String, width: Int, height: Int): Boolean =
    openPopup(url, width, height)

private fun openPopup(url: String, width: Int, height: Int): Boolean =
    js(
        """{
            try {
                var features = 'width=' + width + ',height=' + height +
                    ',menubar=no,toolbar=no,location=no,status=no,resizable=yes';
                var win = window.open(url, '_blank', features);
                return !!win;
            } catch (e) {
                return false;
            }
        }"""
    )

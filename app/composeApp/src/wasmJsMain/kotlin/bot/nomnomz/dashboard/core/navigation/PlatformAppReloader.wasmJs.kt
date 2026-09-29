// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

@file:OptIn(kotlin.js.ExperimentalWasmJsInterop::class)

package bot.nomnomz.dashboard.core.navigation

import kotlinx.browser.window

actual class PlatformAppReloader : AppReloader {

    actual override fun currentLocation(): String = window.location.hash

    // Rewrite the current history entry first (no query string: one-shot return markers must not replay), then
    // reload — the new page boots from scratch at [location].
    actual override fun reload(location: String) {
        window.history.replaceState(null, "", window.location.pathname + location)
        window.location.reload()
    }
}

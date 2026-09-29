// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.navigation

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// Desktop has no page to reload: bump the restart epoch, and the window (Main.kt) rebuilds the app — and its whole
// AppGraph — under it. Desktop has no route history either, so [location] only matters on web.
actual class PlatformAppReloader : AppReloader {

    actual override fun currentLocation(): String = ""

    actual override fun reload(location: String) {
        DesktopAppRestart.request()
    }
}

/** The desktop restart signal: every change rebuilds the app from scratch. */
object DesktopAppRestart {
    private val _epoch: MutableStateFlow<Int> = MutableStateFlow(0)

    val epoch: StateFlow<Int> = _epoch.asStateFlow()

    fun request() {
        _epoch.value += 1
    }
}

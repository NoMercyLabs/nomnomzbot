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

/** An [AppReloader] that records every reload (and the location it opened) instead of tearing the test down. */
class RecordingAppReloader(var location: String = "") : AppReloader {
    val reloads: MutableList<String> = mutableListOf()

    override fun currentLocation(): String = location

    override fun reload(location: String) {
        reloads += location
    }
}

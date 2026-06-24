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

import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.emptyFlow

// Desktop has no reloadable address bar and no Back/Forward history, so route persistence is a non-event: the
// process keeps the live Compose state across the whole run and there is nothing to restore against. The store
// therefore holds the last route in memory purely to keep the seam identical to web — [initialRoute] opens on
// the dashboard like a cold start, and [externalChanges] never emits.
actual class RouteStore actual constructor() {

    private var current: ShellRoute = ShellRoute.Dashboard

    actual fun initialRoute(): ShellRoute = current

    actual fun save(route: ShellRoute) {
        current = route
    }

    actual val externalChanges: Flow<ShellRoute> = emptyFlow()
}

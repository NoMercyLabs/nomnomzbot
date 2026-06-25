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

// A minimal URL-sync seam for the shell's selected page (NOT a Navigation Compose NavHost — that is a
// deferred slice). The shell reads [initialRoute] once to seed its selection from the address bar instead of
// always opening on the dashboard, [save]s every change so a web reload restores the same page, and collects
// [externalChanges] so browser Back/Forward move the selection too.
//
// The seam exists because only the web build has a reloadable, navigable address bar:
//   - wasmJs writes/reads `window.location.hash` (`#/<slug>`) and listens to `popstate`;
//   - jvm has no reload and no URL, so it holds the route in memory and emits nothing — the seam stays
//     identical across targets, the shell never branches on platform.
expect class RouteStore() {

    /** The route to open on first render — parsed from the current URL/saved value, [ShellRoute.Dashboard] when absent. */
    fun initialRoute(): ShellRoute

    /** Persist [route] as the current location so a reload (web) restores it. A no-op storage write on jvm. */
    fun save(route: ShellRoute)

    /**
     * Push the connect screen as a history entry (`#/`) so a browser Back press from inside the shell returns
     * to the connect / signed-out state instead of leaving the app. Called once when Connect → Shell. No-op on
     * jvm (desktop has no browser history).
     */
    fun pushConnectEntry()

    /** Routes the user reached OUTSIDE the app — i.e. browser Back/Forward on web. Never emits on jvm. */
    val externalChanges: Flow<ShellRoute>

    /**
     * Fires when the browser Back/Forward lands on the bare `#/` entry pushed by [pushConnectEntry] — the
     * signal to sign the operator out and return to the Connect screen. Never emits on jvm.
     */
    val disconnectRequests: Flow<Unit>
}

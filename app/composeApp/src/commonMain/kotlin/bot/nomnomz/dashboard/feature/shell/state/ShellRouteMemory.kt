// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.shell.state

import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute

/**
 * The shell's saved-route rule. The saved route (web: the address-bar hash and its history entries) belongs to the
 * signed-in account. An act-as session never reads it (the target opens on their own default page), never writes
 * it (the target's pages never enter the operator's history), and never follows browser Back/Forward through it.
 * Exit reloads the app at the page the operator started acting from.
 */
class ShellRouteMemory(
    private val readSaved: () -> ShellRoute,
    private val writeSaved: (ShellRoute) -> Unit,
    private val sessionStore: SessionStore,
) {
    /** The page the shell opens on. */
    fun openingRoute(): ShellRoute = if (sessionStore.isActingAs) ShellRoute.Dashboard else readSaved()

    /** Save [route] as the current page — never while acting. */
    fun save(route: ShellRoute) {
        if (!sessionStore.isActingAs) writeSaved(route)
    }

    /** The page a browser Back/Forward move asks for, or null when the move must not steer the shell. */
    fun externalMove(route: ShellRoute): ShellRoute? = if (sessionStore.isActingAs) null else route
}

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

/**
 * Tears the whole app down and boots it again — the identity swap for admin act-as. A reload drops every
 * in-memory cache, controller, open socket and back-stack entry at once, so nothing loaded for one identity can
 * reach the next; the boot then resolves the session exactly like a returning sign-in.
 */
interface AppReloader {
    /** Where the app is right now (web: the route hash; empty when there is none). */
    fun currentLocation(): String

    /** Reload the app and open it at [location] (empty = the signed-in user's default landing page). */
    fun reload(location: String)
}

/**
 * The per-target reload:
 *   Web:     a real page reload of the tab.
 *   Desktop: an in-process restart — the window stays, the whole app graph under it is rebuilt.
 */
expect class PlatformAppReloader() : AppReloader {
    override fun currentLocation(): String

    override fun reload(location: String)
}

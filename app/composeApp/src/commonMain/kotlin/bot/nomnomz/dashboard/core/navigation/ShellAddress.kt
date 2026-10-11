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

import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute

// The browser address of the dashboard: `#/c/<login>/<page>[/<tab>]`. The login says which channel the page is
// for; an address without `c/<login>/` is for whichever channel is active. The page and tab come from
// [ShellRouteSlug]. Any address that names no known page opens Home.
data class ShellAddress(val login: String?, val route: ShellRoute) {

    /** The path after `#/`, with the channel first when [login] is known. */
    fun format(): String {
        val page: String = ShellRouteSlug.of(route)
        return if (login == null) page else "$CHANNEL_SEGMENT/$login/$page"
    }

    companion object {
        private const val CHANNEL_SEGMENT: String = "c"

        /** The address a path after `#/` names; a blank or unknown path gives Home with no channel. */
        fun parse(path: String): ShellAddress {
            val segments: List<String> = path.trim().trim('/').split('/')
            if (segments.first().lowercase() != CHANNEL_SEGMENT) {
                return ShellAddress(login = null, route = ShellRouteSlug.parse(path))
            }
            val login: String? = segments.getOrNull(1)?.trim()?.lowercase()?.takeIf { it.isNotEmpty() }
            val page: String = segments.drop(2).joinToString("/")
            return ShellAddress(login = login, route = ShellRouteSlug.parse(page))
        }
    }
}

/** The id of the channel whose login is [login] (any letter case), or null when the roster has none. */
fun channelIdForLogin(roster: List<ChannelSummary>, login: String): String? =
    if (login.isBlank()) null else roster.firstOrNull { it.login.equals(login.trim(), ignoreCase = true) }?.id

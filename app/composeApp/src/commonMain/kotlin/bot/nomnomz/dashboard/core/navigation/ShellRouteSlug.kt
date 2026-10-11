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

// The single source of truth mapping each [ShellRoute] to the address slug that survives a web reload (see
// [RouteStore]): `<page>` for a page without tabs, `<page>/<tab>` for a page with tabs. A page slug alone names
// the first tab of that page. Slugs are an internal, machine-stable contract, never user-facing copy.
// Nothing here knows an older address: an address that names no current page is simply unknown.
object ShellRouteSlug {

    /** The slug written to the address for [route]; the tab is always written when the page has tabs. */
    fun of(route: ShellRoute): String {
        val tab: String? = route.tab
        return if (tab == null) route.page.slug else "${route.page.slug}/$tab"
    }

    /** The route a slug names, or [ShellRoute.Home] when the slug is blank or names no page or tab. */
    fun parse(slug: String): ShellRoute = find(slug) ?: ShellRoute.Home

    /** The route a slug names, or null when it names none, for callers where a fallback page would be wrong. */
    fun find(slug: String): ShellRoute? {
        val segments: List<String> = slug.trim().trim('/').lowercase().split('/')
        val page: ShellPage = ShellPage.entries.firstOrNull { it.slug == segments.first() } ?: return null
        return when (segments.size) {
            1 -> ShellRoute.defaultFor(page)
            2 -> ShellRoute.of(page, segments[1])
            else -> null
        }
    }
}

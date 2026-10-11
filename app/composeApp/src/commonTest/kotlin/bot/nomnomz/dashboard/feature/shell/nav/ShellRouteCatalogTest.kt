// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.shell.nav

import bot.nomnomz.dashboard.core.navigation.ShellPage
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue

// The page registry (ShellPage) and the route enum (ShellRoute) must describe the same destinations: a tab with no
// route would be a tab that opens nothing, a route with an undeclared tab would be an address no tab host shows.
class ShellRouteCatalogTest {

    @Test
    fun every_route_tab_is_listed_on_its_page() {
        ShellRoute.entries.forEach { route ->
            val tab: String? = route.tab
            if (tab != null) {
                assertTrue(tab in route.page.tabs, "$route tab '$tab' is not declared on ${route.page}")
            } else {
                assertTrue(route.page.tabs.isEmpty(), "$route has no tab but ${route.page} declares tabs")
            }
        }
    }

    @Test
    fun every_page_tab_has_exactly_one_route() {
        ShellPage.entries.forEach { page ->
            page.tabs.forEach { tab ->
                val routes: List<ShellRoute> = ShellRoute.entries.filter { it.page == page && it.tab == tab }
                assertEquals(1, routes.size, "${page.slug}/$tab has ${routes.size} routes: $routes")
            }
        }
    }

    @Test
    fun a_page_without_tabs_has_exactly_one_route() {
        ShellPage.entries.filter { it.tabs.isEmpty() }.forEach { page ->
            val routes: List<ShellRoute> = ShellRoute.entries.filter { it.page == page }
            assertEquals(1, routes.size, "${page.slug} has ${routes.size} routes: $routes")
        }
    }

    @Test
    fun page_slugs_are_unique_and_lower_kebab_case() {
        val slugs: List<String> = ShellPage.entries.map { it.slug }
        assertEquals(slugs.size, slugs.toSet().size)
        slugs.forEach { slug -> assertTrue(Regex("[a-z0-9]+(-[a-z0-9]+)*").matches(slug), "bad slug '$slug'") }
    }

    @Test
    fun routes_are_found_by_page_and_tab_and_the_first_tab_is_the_default() {
        assertEquals(ShellRoute.PeopleRules, ShellRoute.of(ShellPage.People, "rules"))
        assertNull(ShellRoute.of(ShellPage.People, "no-such-tab"))
        assertEquals(ShellRoute.PeopleOverview, ShellRoute.defaultFor(ShellPage.People))
        assertEquals(ShellRoute.Commands, ShellRoute.defaultFor(ShellPage.Commands))
    }

    @Test
    fun the_tabs_of_a_page_are_its_routes_in_declared_order() {
        assertEquals(
            listOf(ShellRoute.LibrarySounds, ShellRoute.LibraryFiles, ShellRoute.LibraryRandomResponses, ShellRoute.LibraryQuotes),
            ShellRoute.tabsOf(ShellPage.Library),
        )
    }
}

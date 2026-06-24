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
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

// Proves the URL <-> route contract the web reload depends on (RouteStore). If a slug collided or stopped
// round-tripping, a reload would silently land the operator on the wrong page (or always the dashboard) — so
// these are the behaviours, not the surface. Iterating ShellRoute.entries keeps the proof exhaustive as new
// routes are added to the enum: a missing/duplicate slug fails here without anyone editing the test.
class ShellRouteSlugTest {

    @Test
    fun every_route_round_trips_through_its_slug() {
        ShellRoute.entries.forEach { route ->
            val slug: String = ShellRouteSlug.of(route)
            assertTrue(slug.isNotBlank(), "blank slug for $route")
            assertEquals(route, ShellRouteSlug.parse(slug), "slug '$slug' did not round-trip to $route")
        }
    }

    @Test
    fun slugs_are_unique_per_route() {
        val slugs: List<String> = ShellRoute.entries.map { ShellRouteSlug.of(it) }

        assertEquals(
            slugs.size,
            slugs.toSet().size,
            "duplicate slugs would make a URL ambiguous: $slugs",
        )
    }

    @Test
    fun an_empty_slug_falls_back_to_the_dashboard_landing_page() {
        assertEquals(ShellRoute.Dashboard, ShellRouteSlug.parse(""))
        assertEquals(ShellRoute.Dashboard, ShellRouteSlug.parse("   "))
    }

    @Test
    fun an_unknown_slug_falls_back_to_the_dashboard_landing_page() {
        // A stale slug from a removed route, or a hand-typed typo, must not break the load.
        assertEquals(ShellRoute.Dashboard, ShellRouteSlug.parse("not-a-real-page"))
    }

    @Test
    fun parsing_ignores_surrounding_whitespace_and_case() {
        // The address bar can hand back mixed case / stray spaces (e.g. a pasted URL); the same route must resolve.
        val route: ShellRoute = ShellRoute.Commands
        val canonical: String = ShellRouteSlug.of(route)

        assertEquals(route, ShellRouteSlug.parse(canonical.uppercase()))
        assertEquals(route, ShellRouteSlug.parse("  $canonical  "))
    }
}

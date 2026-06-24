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

// The single source of truth mapping each [ShellRoute] to the URL slug that survives a web reload (see
// [RouteStore]). Slugs are an internal, machine-stable contract — never user-facing copy — so they derive
// deterministically from the route's enum name (lower-cased) rather than a localized label, which keeps the
// mapping exhaustive for FREE as new routes are added to the enum (no per-route line to forget).
//
// Two invariants the round-trip relies on, both proven in ShellRouteSlugTest:
//   - every slug is unique (the enum names are, lower-casing preserves that), so a slug resolves to one route;
//   - the landing page [ShellRoute.Dashboard] also answers to the EMPTY slug, so a bare `#/` (or no hash at
//     all) lands on the dashboard — the same default the shell starts from.
object ShellRouteSlug {

    /** The slug written to the URL for [route] — its lower-cased enum name (e.g. SongRequests -> "songrequests"). */
    fun of(route: ShellRoute): String = route.name.lowercase()

    /**
     * The route a slug names, or [ShellRoute.Dashboard] when the slug is blank or matches nothing — so an empty
     * `#/`, a stale slug from a removed route, or a typo all fall back to the landing page instead of failing.
     */
    fun parse(slug: String): ShellRoute {
        val normalized: String = slug.trim().lowercase()
        if (normalized.isEmpty()) return ShellRoute.Dashboard
        return BY_SLUG[normalized] ?: ShellRoute.Dashboard
    }

    private val BY_SLUG: Map<String, ShellRoute> =
        ShellRoute.entries.associateBy { route -> of(route) }
}

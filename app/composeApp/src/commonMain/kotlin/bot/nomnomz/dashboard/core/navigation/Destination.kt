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

// The top-level destinations of the FOUNDATION spine. The gate in App.kt resolves these
// from the boot timer + the session phase, in the order Splash -> Connect -> Setup -> Shell. The
// public marketing pitch is the fast static page the API serves at `/` (Api/Assets/landing) —
// its "Get started" CTA links straight to `/app`, so the booted Compose bundle goes directly to the
// sign-in card instead of repeating the same pitch a second time behind an extra click.
//
// Next slice replaces this with the full type-safe `@Serializable sealed interface Route`
// graph + Navigation Compose NavHost (frontend.md §5).
enum class Destination {
    Splash,
    Connect,
    Setup,
    Shell,

    /**
     * A remembered session exists but the backend could not be reached to confirm it (S050 — "remembered-session
     * vs unreachable distinction"). Distinct from [Connect]: that means "no session, sign in"; this means "you
     * have a session, we just cannot reach your bot right now" — never conflate the two.
     */
    Unreachable,
}

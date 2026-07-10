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
// from the boot timer + the session phase, in the order Splash -> Landing -> Connect -> Setup -> Shell.
// Landing is the public front page shown to a booted-but-not-connected visitor; its "Get started" CTA
// advances to Connect (the sign-in card).
//
// Next slice replaces this with the full type-safe `@Serializable sealed interface Route`
// graph + Navigation Compose NavHost (frontend.md §5).
enum class Destination {
    Splash,
    Landing,
    Connect,
    Setup,
    Shell,
}

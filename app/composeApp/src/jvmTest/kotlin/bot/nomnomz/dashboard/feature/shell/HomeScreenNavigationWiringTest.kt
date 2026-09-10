// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.shell

import java.io.File
import kotlin.test.Test
import kotlin.test.fail

// ShellScreen.kt's ShellRoute.Dashboard branch constructs HomeScreen() directly, inside a private dispatch
// table that can't be exercised without standing up the whole AppGraph — so this guards it by source, the
// same way ShellProvidesWindowSizeClassTest guards its neighboring shell-wiring rule.
//
// The regression (owner report 2026-09-10, found via the unmanaged-rewards "Review" button doing nothing):
// HomeScreen's onNavigate parameter carries a `= {}` no-op default, and the ShellRoute.Dashboard call site
// never passed one. Every route-based action on Home — the Review button for rewards/dead-integration-token/
// held-message attention items (via attentionRouteFor), each first-run checklist step, and the Analytics
// "View all" link — silently did nothing, compiling clean because the default swallowed every call.
class HomeScreenNavigationWiringTest {

    private val shell: File =
        File("src/commonMain/kotlin/bot/nomnomz/dashboard/feature/shell/ui/ShellScreen.kt")

    @Test
    fun the_dashboard_route_passes_onNavigate_to_home_screen() {
        val source: String = shell.readText()

        val callAt: Int = source.indexOf("ShellRoute.Dashboard -> HomeScreen(")
        if (callAt < 0) fail("ShellRoute.Dashboard no longer constructs HomeScreen — this guard needs rewriting")

        val callEnd: Int = source.indexOf("\n            )", callAt)
        if (callEnd < 0) fail("Could not find the end of the HomeScreen(...) call to check its arguments")

        val call: String = source.substring(callAt, callEnd)
        if (!call.contains("onNavigate =")) {
            fail(
                "ShellRoute.Dashboard's HomeScreen(...) call no longer passes onNavigate — it falls back to " +
                    "HomeScreen's no-op default, and every route-based Review/checklist/\"View all\" button " +
                    "on the Home page does nothing again."
            )
        }
    }
}

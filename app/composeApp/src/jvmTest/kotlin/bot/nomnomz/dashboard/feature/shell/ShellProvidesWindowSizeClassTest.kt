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

// Every screen branches its layout on LocalWindowSizeClass, so WHERE the shell measures that width decides
// whether those branches are right.
//
// It must be measured on the CONTENT PANE, inside ShellContent, after the persistent sidebar has taken its
// fixed width. Measured on the window instead, a 900 dp window reports Expanded while the sidebar leaves the
// screen roughly 660 dp - and every screen then lays out for room it does not have. The bug is invisible in a
// maximised window and appears only at the widths between the two, which is exactly the kind of thing that
// ships.
class ShellProvidesWindowSizeClassTest {

    private val shell: File =
        File("src/commonMain/kotlin/bot/nomnomz/dashboard/feature/shell/ui/ShellScreen.kt")

    @Test
    fun the_size_class_is_provided_from_the_content_pane_not_the_window() {
        val source: String = shell.readText()

        val provideAt: Int = source.indexOf("CompositionLocalProvider(LocalWindowSizeClass provides")
        if (provideAt < 0) {
            fail("the shell no longer provides LocalWindowSizeClass — every screen would fall back to Expanded")
        }

        val contentAt: Int = source.indexOf("private fun ShellContent(")
        if (contentAt < 0) fail("ShellContent is gone — this guard needs rewriting for the new shell shape")

        if (provideAt < contentAt) {
            fail(
                "LocalWindowSizeClass is provided ABOVE ShellContent, so it measures the whole window " +
                    "including the sidebar. Screens would be told they have room the sidebar has already " +
                    "taken. Provide it inside ShellContent, from the content pane's own constraints."
            )
        }
    }

    @Test
    fun the_provided_width_is_measured_and_not_a_constant() {
        // `provides WindowSizeClass.Expanded` (or any literal) would compile, pass a smoke test, and pin every
        // viewport to the desktop layout — the failure mode that makes a responsive layer decorative.
        val source: String = shell.readText()

        if (!source.contains("LocalWindowSizeClass provides WindowSizeClass.of(maxWidth)")) {
            fail(
                "the shell must provide WindowSizeClass.of(maxWidth) from a BoxWithConstraints around the " +
                    "content pane — a constant or a value from anywhere else stops the layout responding at all"
            )
        }
    }
}

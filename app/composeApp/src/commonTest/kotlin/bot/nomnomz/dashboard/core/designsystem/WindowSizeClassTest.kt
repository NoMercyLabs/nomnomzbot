// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem

import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.theme.WindowSizeClass
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

// `frontend-design-system.md` §6 promises Material's standard widths - Compact < 600, Medium 600-839,
// Expanded >= 840 - and the mobile-reuse guarantee rests on them: shipping mobile adds Compact layouts and
// rewrites no components. An off-by-one at a boundary silently gives a 600 dp tablet the phone layout, which
// nobody notices until someone opens it on a tablet.
class WindowSizeClassTest {

    @Test
    fun the_boundaries_are_the_documented_widths_and_are_inclusive_at_the_bottom() {
        assertEquals(WindowSizeClass.Compact, WindowSizeClass.of(599.dp))
        // 600 is Medium, not the last Compact width - the boundary belongs to the wider class.
        assertEquals(WindowSizeClass.Medium, WindowSizeClass.of(600.dp))
        assertEquals(WindowSizeClass.Medium, WindowSizeClass.of(839.dp))
        assertEquals(WindowSizeClass.Expanded, WindowSizeClass.of(840.dp))
    }

    @Test
    fun a_vanishing_or_absurd_width_still_resolves_to_a_class() {
        // Compose hands out a zero width for a frame during layout, and a screen must not crash or blank on it.
        assertEquals(WindowSizeClass.Compact, WindowSizeClass.of(0.dp))
        assertEquals(WindowSizeClass.Expanded, WindowSizeClass.of(10_000.dp))
    }

    @Test
    fun the_convenience_flags_agree_with_the_class_they_describe() {
        // These are what call sites actually read, so a flag that disagreed with its class would send a
        // screen down the wrong layout branch while the class itself looked correct in a debugger.
        assertTrue(WindowSizeClass.Compact.isCompact)
        assertFalse(WindowSizeClass.Compact.isAtLeastMedium)
        assertFalse(WindowSizeClass.Compact.isExpanded)

        assertFalse(WindowSizeClass.Medium.isCompact)
        assertTrue(WindowSizeClass.Medium.isAtLeastMedium)
        assertFalse(WindowSizeClass.Medium.isExpanded)

        assertFalse(WindowSizeClass.Expanded.isCompact)
        assertTrue(WindowSizeClass.Expanded.isAtLeastMedium)
        assertTrue(WindowSizeClass.Expanded.isExpanded)
    }

    @Test
    fun every_class_is_reachable_so_none_is_dead_code() {
        val reached: Set<WindowSizeClass> =
            listOf(0, 599, 600, 839, 840, 4000).map { WindowSizeClass.of(it.dp) }.toSet()

        assertEquals(WindowSizeClass.entries.toSet(), reached)
    }
}

// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.component

import bot.nomnomz.dashboard.core.designsystem.theme.ControlPalette
import kotlin.test.Test
import kotlin.test.assertEquals

class SelectionControlsTest {

    @Test
    fun selection_border_reflects_idle_and_interactive_states() {
        assertEquals(
            ControlPalette.InactiveOutline,
            selectionOutline(
                hovered = false,
                focused = false,
                enabled = true,
            ),
        )
        assertEquals(
            ControlPalette.InactiveOutline,
            selectionOutline(
                hovered = true,
                focused = false,
                enabled = true,
            ),
        )
        assertEquals(
            ControlPalette.Focus,
            selectionOutline(
                hovered = false,
                focused = true,
                enabled = true,
            ),
        )
    }

    @Test
    fun disabled_state_takes_precedence_over_selection_and_focus() {
        assertEquals(
            ControlPalette.InactiveOutline,
            selectionOutline(
                hovered = true,
                focused = true,
                enabled = false,
            ),
        )
    }
}

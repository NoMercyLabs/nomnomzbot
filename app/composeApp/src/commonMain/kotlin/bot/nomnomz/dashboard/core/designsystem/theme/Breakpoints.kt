// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.theme

import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp

/**
 * Shared window-width breakpoints for layout branches (a window concern, distinct from the
 * [Spacing] scale): feature screens branch on these instead of carrying private dp constants,
 * so every width-based layout switch in the app happens at the same few widths.
 */
internal object Breakpoints {
    /**
     * Below this WINDOW width a persistent sidebar shell collapses to its compact (drawer) layout.
     *
     * Deliberately not one of the [WindowSizeClass] widths below, and not a screen-layout decision: this
     * asks whether there is room for the sidebar AND a usable content pane beside it, which needs more
     * width than the content alone. A screen must branch on [LocalWindowSizeClass] — the size of the space
     * it was actually given — never on this.
     */
    val Compact: Dp = 720.dp

    /**
     * Below this WINDOW height a persistent sidebar shell also collapses to its drawer layout, whatever the
     * width is.
     *
     * A phone in landscape is WIDE and SHORT — 844 x 390 on a common handset — so a width-only rule hands it
     * the desktop sidebar and leaves 390 dp of height for a full-height nav column plus content. Height is
     * the binding constraint there, not width, and only checking width is why landscape phones get a layout
     * meant for a monitor.
     */
    val CompactHeight: Dp = 500.dp

    /** Below this width wide multi-column strips (e.g. the 8 stat tiles) scroll instead of squeezing. */
    val Wide: Dp = 960.dp

    /** Material's standard breakpoints, the vocabulary [WindowSizeClass] is built from. */
    val MediumMinWidth: Dp = 600.dp

    /** Material's standard breakpoints, the vocabulary [WindowSizeClass] is built from. */
    val ExpandedMinWidth: Dp = 840.dp
}

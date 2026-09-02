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
    /** Below this width a persistent sidebar shell collapses to its compact (drawer) layout. */
    val Compact: Dp = 720.dp

    /** Below this width wide multi-column strips (e.g. the 8 stat tiles) scroll instead of squeezing. */
    val Wide: Dp = 960.dp
}

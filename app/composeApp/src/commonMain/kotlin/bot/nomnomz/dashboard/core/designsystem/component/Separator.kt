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

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.width
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens

// A 1px hairline — a stroke, not a layout spacing value, so it is intentionally not in Space.*.
private val SeparatorThickness: Dp = 1.dp

/** shadcn Separator orientation (frontend-design-system.md §4, catalogue row). */
enum class SeparatorOrientation {
    Horizontal,
    Vertical,
}

/**
 * shadcn/ui Separator ported to Compose (frontend-design-system.md §4, catalogue row).
 *
 * Foundation-based — a single [Tokens.border]-coloured hairline. Horizontal fills the available
 * width; vertical fills the available height. Decorative by default; the divider carries no
 * semantics, so no a11y role is set. Drop-in replacement for Material3's `HorizontalDivider`
 * (default orientation) — call sites only need an import swap.
 */
@Composable
fun Separator(
    modifier: Modifier = Modifier,
    orientation: SeparatorOrientation = SeparatorOrientation.Horizontal,
) {
    val tokens: Tokens = LocalTokens.current

    val sizeModifier: Modifier =
        when (orientation) {
            SeparatorOrientation.Horizontal -> Modifier.fillMaxWidth().height(SeparatorThickness)
            SeparatorOrientation.Vertical -> Modifier.fillMaxHeight().width(SeparatorThickness)
        }

    Box(modifier = modifier.then(sizeModifier).background(tokens.border))
}

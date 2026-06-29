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
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens

// 1dp border stroke — not a layout spacing value.
private val CardBorderWidth: Dp = 1.dp

/**
 * shadcn/ui Card ported to Compose (frontend-design-system.md §4, catalogue row).
 *
 * Foundation-based (no Material surface, no elevation). Uses [Tokens.card] background,
 * [Tokens.border] stroke, and [Tokens.radius.lg] corners — the exact shadcn card contract.
 * The content lambda receives [ColumnScope] to match Material3's `Card` call-site ergonomics
 * so existing screens only need an import swap.
 */
@Composable
fun Card(
    modifier: Modifier = Modifier,
    content: @Composable ColumnScope.() -> Unit,
) {
    val tokens: Tokens = LocalTokens.current
    val shape: RoundedCornerShape = RoundedCornerShape(tokens.radius.lg)

    Column(
        modifier =
            modifier
                .border(width = CardBorderWidth, color = tokens.border, shape = shape)
                .clip(shape)
                .background(color = tokens.card),
        content = content,
    )
}

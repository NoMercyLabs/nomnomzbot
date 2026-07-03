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

import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.PlainTooltip
import androidx.compose.material3.Text
import androidx.compose.material3.TooltipBox
import androidx.compose.material3.TooltipDefaults
import androidx.compose.material3.rememberTooltipState
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens
import bot.nomnomz.dashboard.core.designsystem.theme.Typography

/**
 * shadcn/ui Tooltip ported to Compose (frontend-design-system.md §4, catalogue row).
 *
 * A themed wrapper over Material3's `TooltipBox` — the primitive already solves hover/focus
 * triggering, delay and edge-aware positioning — with the bubble recoloured to the shadcn
 * [Tokens.popover] surface. Wrap the anchor in [content]; the tooltip shows [text] on hover/focus.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun Tooltip(
    text: String,
    modifier: Modifier = Modifier,
    content: @Composable () -> Unit,
) {
    val tokens: Tokens = LocalTokens.current
    val typography: Typography = LocalTypography.current

    TooltipBox(
        positionProvider = TooltipDefaults.rememberPlainTooltipPositionProvider(),
        tooltip = {
            PlainTooltip(
                containerColor = tokens.popover,
                contentColor = tokens.popoverForeground,
            ) {
                Text(text = text, style = typography.xs)
            }
        },
        state = rememberTooltipState(),
        modifier = modifier,
        content = content,
    )
}

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
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Popup
import androidx.compose.ui.window.PopupProperties
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.Spacing
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens

private val PopoverBorderWidth: Dp = 1.dp
private val PopoverShadowElevation: Dp = 12.dp

/**
 * shadcn/ui Popover ported to Compose (frontend-design-system.md §4, catalogue row).
 *
 * A generic anchored overlay — Foundation's [Popup], not menu semantics — for arbitrary content
 * (forms, previews, free-form panels) rather than [DropdownMenu]'s list-of-items case. Styled with
 * the same [Tokens.popover] surface + [Tokens.border] hairline as [DropdownMenu] so the two overlay
 * primitives read as one family; unlike `DropdownMenu`, the caller supplies any composable content,
 * not just [DropdownMenuItem] rows, and the caller owns the open/close flag ([expanded]).
 */
@Composable
fun Popover(
    expanded: Boolean,
    onDismissRequest: () -> Unit,
    modifier: Modifier = Modifier,
    alignment: Alignment = Alignment.TopStart,
    offset: IntOffset = IntOffset.Zero,
    content: @Composable () -> Unit,
) {
    if (!expanded) return

    val tokens: Tokens = LocalTokens.current
    val spacing: Spacing = LocalSpacing.current
    val shape: RoundedCornerShape = RoundedCornerShape(tokens.radius.md)

    Popup(
        alignment = alignment,
        offset = offset,
        onDismissRequest = onDismissRequest,
        properties = PopupProperties(focusable = true),
    ) {
        Column(
            modifier =
                modifier
                    .shadow(elevation = PopoverShadowElevation, shape = shape)
                    .clip(shape)
                    .background(tokens.popover)
                    .border(PopoverBorderWidth, tokens.border, shape)
                    .padding(spacing.s3),
        ) {
            content()
        }
    }
}

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

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.DropdownMenu as Material3DropdownMenu
import androidx.compose.material3.DropdownMenuItem as Material3DropdownMenuItem
import androidx.compose.material3.MenuDefaults
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens

// 1dp border stroke — not a layout spacing value.
private val MenuBorderWidth: Dp = 1.dp

/**
 * shadcn/ui DropdownMenu ported to Compose (frontend-design-system.md §4, catalogue row).
 *
 * A themed wrapper over Material3's `DropdownMenu` — the a11y-correct menu primitive (DS7
 * "M3-wrapped, menu semantics") — recoloured to the shadcn [Tokens.popover] surface with a
 * [Tokens.border] hairline. Same call signature as `androidx.compose.material3.DropdownMenu`
 * (pair with [DropdownMenuItem]), so call sites only need an import swap.
 */
@Composable
fun DropdownMenu(
    expanded: Boolean,
    onDismissRequest: () -> Unit,
    modifier: Modifier = Modifier,
    content: @Composable androidx.compose.foundation.layout.ColumnScope.() -> Unit,
) {
    val tokens: Tokens = LocalTokens.current
    Material3DropdownMenu(
        expanded = expanded,
        onDismissRequest = onDismissRequest,
        modifier = modifier,
        shape = RoundedCornerShape(tokens.radius.md),
        containerColor = tokens.popover,
        border = BorderStroke(MenuBorderWidth, tokens.border),
        content = content,
    )
}

/**
 * shadcn `DropdownMenuItem` — a single selectable row inside a [DropdownMenu]. Themed to
 * [Tokens.popoverForeground]; matches Material3's `DropdownMenuItem` signature for an import swap.
 */
@Composable
fun DropdownMenuItem(
    text: @Composable () -> Unit,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    leadingIcon: (@Composable () -> Unit)? = null,
    trailingIcon: (@Composable () -> Unit)? = null,
    enabled: Boolean = true,
) {
    val tokens: Tokens = LocalTokens.current
    Material3DropdownMenuItem(
        text = text,
        onClick = onClick,
        modifier = modifier,
        leadingIcon = leadingIcon,
        trailingIcon = trailingIcon,
        enabled = enabled,
        colors =
            MenuDefaults.itemColors(
                textColor = tokens.popoverForeground,
                leadingIconColor = tokens.mutedForeground,
                trailingIconColor = tokens.mutedForeground,
                disabledTextColor = tokens.mutedForeground,
            ),
    )
}

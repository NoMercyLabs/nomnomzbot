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

import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.hoverable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.interaction.collectIsHoveredAsState
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.LocalContentColor
import androidx.compose.material3.LocalTextStyle
import androidx.compose.material3.MenuDefaults
import androidx.compose.material3.DropdownMenu as Material3DropdownMenu
import androidx.compose.material3.DropdownMenuItem as Material3DropdownMenuItem
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.PointerIcon
import androidx.compose.ui.input.pointer.pointerHoverIcon
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.theme.ControlPalette
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography

private val MenuBorderWidth: Dp = 1.dp
private val MenuRadius: Dp = 18.dp
private val MenuItemRadius: Dp = 12.dp
private val MenuItemHeight: Dp = 48.dp
private val MenuShadowElevation: Dp = 12.dp
private const val MenuTransitionMillis: Int = 120

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
    Material3DropdownMenu(
        expanded = expanded,
        onDismissRequest = onDismissRequest,
        modifier = modifier,
        shape = RoundedCornerShape(MenuRadius),
        containerColor = ControlPalette.Surface,
        tonalElevation = 0.dp,
        shadowElevation = MenuShadowElevation,
        border = BorderStroke(MenuBorderWidth, ControlPalette.LilacWhite.copy(alpha = 0.12f)),
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
    selected: Boolean = false,
    destructive: Boolean = false,
) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val interactionSource = remember { MutableInteractionSource() }
    val hovered: Boolean by interactionSource.collectIsHoveredAsState()
    val focused: Boolean by interactionSource.collectIsFocusedAsState()
    val active = enabled && (hovered || focused)
    val backgroundTarget =
        when {
            active && destructive -> ControlPalette.DestructiveTint.copy(alpha = 0.14f)
            active -> ControlPalette.White.copy(alpha = 0.10f)
            selected -> ControlPalette.SurfaceRaised
            else -> Color.Transparent
        }
    val contentTarget =
        when {
            destructive -> ControlPalette.DestructiveContent
            active || selected -> ControlPalette.White
            else -> ControlPalette.LilacWhite.copy(alpha = 0.72f)
        }
    val background: Color by
        animateColorAsState(backgroundTarget, tween(MenuTransitionMillis), label = "menuItemBackground")
    val contentColor: Color by
        animateColorAsState(contentTarget, tween(MenuTransitionMillis), label = "menuItemContent")
    val shape = RoundedCornerShape(MenuItemRadius)

    CompositionLocalProvider(
        LocalContentColor provides contentColor,
        LocalTextStyle provides typography.sm.copy(color = contentColor),
    ) {
        // Keep Material's menu-item primitive underneath the NomNomz visuals. It owns menu semantics,
        // focus traversal, keyboard activation, disabled behavior, and the click target; the outer
        // modifiers only supply the Figma surface, hover, and geometry.
        Material3DropdownMenuItem(
            text = text,
            onClick = onClick,
            leadingIcon = leadingIcon,
            trailingIcon = trailingIcon,
            enabled = enabled,
            modifier =
                modifier
                    .padding(horizontal = spacing.s1_5)
                    .defaultMinSize(minHeight = MenuItemHeight)
                    .clip(shape)
                    .background(background)
                    .hoverable(interactionSource, enabled = enabled)
                    .pointerHoverIcon(if (enabled) PointerIcon.Hand else PointerIcon.Default),
            colors =
                MenuDefaults.itemColors(
                    textColor = contentColor,
                    leadingIconColor = contentColor,
                    trailingIconColor = contentColor,
                    disabledTextColor = contentColor.copy(alpha = 0.48f),
                    disabledLeadingIconColor = contentColor.copy(alpha = 0.48f),
                    disabledTrailingIconColor = contentColor.copy(alpha = 0.48f),
                ),
            contentPadding = PaddingValues(horizontal = spacing.s3),
            interactionSource = interactionSource,
        )
    }
}

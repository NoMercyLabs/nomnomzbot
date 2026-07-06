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
import androidx.compose.foundation.clickable
import androidx.compose.foundation.hoverable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsHoveredAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.LocalContentColor
import androidx.compose.material3.LocalTextStyle
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.PointerIcon
import androidx.compose.ui.input.pointer.pointerHoverIcon
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.Spacing
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens
import bot.nomnomz.dashboard.core.designsystem.theme.Typography

// 1dp border stroke — not a layout spacing value.
private val TabBorderWidth: Dp = 1.dp

/**
 * shadcn/ui Tabs list ported to Compose (frontend-design-system.md §4, catalogue row — `Tabs`
 * parts List/Trigger/Content).
 *
 * Foundation-based: the muted, rounded pill container that holds a row of [TabsTrigger]s. The caller
 * owns the selected-tab state and renders the matching content below the list.
 */
@Composable
fun TabsList(modifier: Modifier = Modifier, content: @Composable RowScope.() -> Unit) {
    val tokens: Tokens = LocalTokens.current
    val spacing: Spacing = LocalSpacing.current
    Row(
        modifier =
            modifier
                .clip(RoundedCornerShape(tokens.radius.md))
                .background(tokens.muted)
                .padding(spacing.s1),
        horizontalArrangement = Arrangement.spacedBy(spacing.s1),
        verticalAlignment = Alignment.CenterVertically,
        content = content,
    )
}

/**
 * shadcn `TabsTrigger` — one selectable tab inside a [TabsList]. Selected renders the raised
 * [Tokens.background] pill with [Tokens.foreground] text; unselected is transparent with
 * [Tokens.mutedForeground] text and an accent hover, matching Button ghost/outline affordance.
 */
@Composable
fun TabsTrigger(
    selected: Boolean,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    content: @Composable RowScope.() -> Unit,
) {
    val tokens: Tokens = LocalTokens.current
    val spacing: Spacing = LocalSpacing.current
    val typography: Typography = LocalTypography.current

    val interactionSource: MutableInteractionSource = remember { MutableInteractionSource() }
    val hovered: Boolean by interactionSource.collectIsHoveredAsState()

    val container: Color =
        when {
            !enabled -> tokens.muted
            selected -> tokens.background
            hovered -> tokens.accent
            else -> Color.Transparent
        }
    val contentColor: Color =
        when {
            !enabled -> tokens.mutedForeground
            selected -> tokens.foreground
            hovered -> tokens.accentForeground
            else -> tokens.mutedForeground
        }
    val borderColor: Color =
        when {
            !enabled || selected || hovered -> Color.Transparent
            else -> tokens.border
        }
    val shape: RoundedCornerShape = RoundedCornerShape(tokens.radius.sm)

    CompositionLocalProvider(
        LocalTextStyle provides typography.sm.copy(fontWeight = FontWeight.Medium, color = contentColor),
        LocalContentColor provides contentColor,
    ) {
        Box(
            modifier =
                modifier
                    .defaultMinSize(minHeight = spacing.s8)
                    .clip(shape)
                    .then(
                        if (borderColor != Color.Transparent) {
                            Modifier.border(TabBorderWidth, borderColor, shape)
                        } else {
                            Modifier
                        }
                    )
                    .background(container)
                    .hoverable(interactionSource)
                    .clickable(
                        interactionSource = interactionSource,
                        indication = null,
                        enabled = enabled,
                        onClick = onClick,
                    )
                    .pointerHoverIcon(if (enabled) PointerIcon.Hand else PointerIcon.Default)
                    .padding(horizontal = spacing.s3, vertical = spacing.s1_5),
            contentAlignment = Alignment.Center,
        ) {
            Row(
                horizontalArrangement = Arrangement.spacedBy(spacing.s1, Alignment.CenterHorizontally),
                verticalAlignment = Alignment.CenterVertically,
                content = content,
            )
        }
    }
}

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
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.LocalContentColor
import androidx.compose.material3.LocalTextStyle
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
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
private val BadgeBorderWidth: Dp = 1.dp

/** shadcn Badge variants (frontend-design-system.md §4, catalogue row). */
enum class BadgeVariant {
    Default,
    Secondary,
    Destructive,
    Outline,
}

private data class BadgeColors(val container: Color, val content: Color, val border: Color)

private fun resolveBadgeColors(variant: BadgeVariant, tokens: Tokens): BadgeColors =
    when (variant) {
        BadgeVariant.Default ->
            BadgeColors(tokens.primary, tokens.primaryForeground, Color.Transparent)
        BadgeVariant.Secondary ->
            BadgeColors(tokens.secondary, tokens.secondaryForeground, Color.Transparent)
        BadgeVariant.Destructive ->
            BadgeColors(tokens.destructive, tokens.destructiveForeground, Color.Transparent)
        BadgeVariant.Outline ->
            BadgeColors(Color.Transparent, tokens.foreground, tokens.border)
    }

/**
 * shadcn/ui Badge ported to Compose (frontend-design-system.md §4, catalogue row).
 *
 * Foundation-based, token-driven. By default a static display chip. When [selected] is non-null
 * the badge becomes a **selectable toggle** (the catalogue's selectable state): `selected = true`
 * renders the requested [variant]; `selected = false` renders the muted `Outline` look. Combined
 * with [onClick], this replaces Material3's `FilterChip` for single-select chip rows — call sites
 * swap the import and pass `selected` / `onClick`.
 */
@Composable
fun Badge(
    modifier: Modifier = Modifier,
    variant: BadgeVariant = BadgeVariant.Default,
    selected: Boolean? = null,
    enabled: Boolean = true,
    onClick: (() -> Unit)? = null,
    content: @Composable RowScope.() -> Unit,
) {
    val tokens: Tokens = LocalTokens.current
    val spacing: Spacing = LocalSpacing.current
    val typography: Typography = LocalTypography.current

    // Selectable badges show the requested variant when on, and fall back to Outline when off.
    val effectiveVariant: BadgeVariant =
        when (selected) {
            null, true -> variant
            false -> BadgeVariant.Outline
        }
    val colors: BadgeColors = resolveBadgeColors(effectiveVariant, tokens)
    val shape: RoundedCornerShape = RoundedCornerShape(tokens.radius.md)

    val interactionSource: MutableInteractionSource = remember { MutableInteractionSource() }

    val clickModifier: Modifier =
        if (onClick != null)
            Modifier
                .clickable(
                    interactionSource = interactionSource,
                    indication = null,
                    enabled = enabled,
                    onClick = onClick,
                )
                .pointerHoverIcon(if (enabled) PointerIcon.Hand else PointerIcon.Default)
        else Modifier

    val textStyle = typography.xs.copy(fontWeight = FontWeight.Medium, color = colors.content)

    CompositionLocalProvider(
        LocalTextStyle provides textStyle,
        LocalContentColor provides colors.content,
    ) {
        Row(
            modifier =
                modifier
                    .then(
                        if (colors.border != Color.Transparent)
                            Modifier.border(BadgeBorderWidth, colors.border, shape)
                        else Modifier
                    )
                    .clip(shape)
                    .background(colors.container)
                    .then(clickModifier)
                    .padding(horizontal = spacing.s2, vertical = spacing.s0_5),
            horizontalArrangement = Arrangement.spacedBy(spacing.s1, Alignment.CenterHorizontally),
            verticalAlignment = Alignment.CenterVertically,
            content = content,
        )
    }
}

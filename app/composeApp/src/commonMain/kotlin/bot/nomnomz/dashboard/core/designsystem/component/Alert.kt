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
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.LocalContentColor
import androidx.compose.material3.LocalTextStyle
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
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
private val AlertBorderWidth: Dp = 1.dp

/** shadcn Alert variants (frontend-design-system.md §4, catalogue row). */
enum class AlertVariant {
    Default,
    Destructive,
}

private data class AlertColors(val container: Color, val border: Color, val content: Color)

private fun resolveAlertColors(variant: AlertVariant, tokens: Tokens): AlertColors =
    when (variant) {
        AlertVariant.Default -> AlertColors(tokens.card, tokens.border, tokens.cardForeground)
        AlertVariant.Destructive ->
            AlertColors(tokens.card, tokens.destructive, tokens.destructive)
    }

/**
 * shadcn/ui Alert ported to Compose (frontend-design-system.md §4, catalogue row).
 *
 * Foundation-based static banner — [Tokens.card] surface, variant-coloured border, and content
 * colour driven by [resolveAlertColors]. Compose the body from [AlertTitle] / [AlertDescription]
 * (and an optional leading icon slot); a bordered card with no elevation, matching shadcn's
 * `role="alert"` static contract (not a toast/dismissible surface — see [Toast] for that later).
 */
@Composable
fun Alert(
    modifier: Modifier = Modifier,
    variant: AlertVariant = AlertVariant.Default,
    icon: (@Composable () -> Unit)? = null,
    content: @Composable ColumnScope.() -> Unit,
) {
    val tokens: Tokens = LocalTokens.current
    val spacing: Spacing = LocalSpacing.current
    val colors: AlertColors = resolveAlertColors(variant, tokens)
    val shape: RoundedCornerShape = RoundedCornerShape(tokens.radius.lg)

    CompositionLocalProvider(LocalContentColor provides colors.content) {
        Row(
            modifier =
                modifier
                    .border(width = AlertBorderWidth, color = colors.border, shape = shape)
                    .clip(shape)
                    .background(colors.container)
                    .padding(spacing.s4),
            verticalAlignment = Alignment.Top,
        ) {
            if (icon != null) {
                Row(modifier = Modifier.padding(end = spacing.s3)) { icon() }
            }
            Column(
                verticalArrangement = Arrangement.spacedBy(spacing.s1),
                content = content,
            )
        }
    }
}

/** [Alert] title slot — bold, [Tokens.cardForeground]-inheriting via [LocalContentColor]. */
@Composable
fun AlertTitle(text: String, modifier: Modifier = Modifier) {
    val typography: Typography = LocalTypography.current
    Text(
        text = text,
        modifier = modifier,
        style = typography.sm.copy(fontWeight = FontWeight.Medium),
        color = LocalContentColor.current,
    )
}

/** [Alert] description slot — muted body copy, inherits the variant content colour. */
@Composable
fun AlertDescription(text: String, modifier: Modifier = Modifier) {
    val tokens: Tokens = LocalTokens.current
    val typography: Typography = LocalTypography.current
    CompositionLocalProvider(LocalTextStyle provides typography.sm) {
        Text(text = text, modifier = modifier, style = typography.sm, color = tokens.mutedForeground)
    }
}

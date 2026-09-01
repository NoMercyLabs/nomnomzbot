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
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.LocalTextStyle
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.icon.ChevronDownGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.Spacing
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens
import bot.nomnomz.dashboard.core.designsystem.theme.Typography
import org.jetbrains.compose.resources.painterResource

// 1dp border stroke — not a layout spacing value (matches Input/AppSelectField's field border).
private val SelectBorderWidth: Dp = 1.dp
private val ExpandedSelectBorderWidth: Dp = 2.dp
private val SelectMinHeight: Dp = 44.dp
private const val SelectTransitionMillis: Int = 150

/**
 * shadcn/ui Select ported to Compose (frontend-design-system.md §4, catalogue row).
 *
 * The M3-wrapped (menu-semantics) single-select primitive: a field-styled trigger — label above,
 * bordered box, trailing chevron — that opens a [DropdownMenu] of [options], one [DropdownMenuItem]
 * per option via [optionLabel] with the current [value] marked `selected`. The trigger is a plain
 * clickable [Row], not a text field, so the whole surface opens the menu (see [AppSelectField],
 * whose trigger chrome this mirrors). This is the closed-catalogue primitive; [AppSelectField]
 * remains the app-composite pattern for cases needing free-form menu content instead of a flat
 * `List<T>`.
 *
 * @param T the option type; [optionLabel] renders it and equality drives the `selected` state.
 */
@Composable
fun <T> Select(
    value: T?,
    options: List<T>,
    onValueChange: (T) -> Unit,
    label: String,
    optionLabel: (T) -> String,
    modifier: Modifier = Modifier,
    expanded: Boolean,
    onExpandedChange: (Boolean) -> Unit,
    enabled: Boolean = true,
    placeholder: String? = null,
    supportingText: String? = null,
) {
    val tokens: Tokens = LocalTokens.current
    val spacing: Spacing = LocalSpacing.current
    val typography: Typography = LocalTypography.current

    val shape: RoundedCornerShape = RoundedCornerShape(tokens.radius.sm)
    val targetBorderColor: Color =
        when {
            !enabled -> tokens.border.copy(alpha = 0.5f)
            expanded -> tokens.ring
            else -> tokens.border
        }
    val borderColor: Color by animateColorAsState(
        targetValue = targetBorderColor,
        animationSpec = tween(SelectTransitionMillis),
        label = "selectBorder",
    )
    val displayText: String = value?.let(optionLabel) ?: (placeholder ?: "")
    val displayColor: Color =
        when {
            !enabled -> tokens.mutedForeground
            value != null -> tokens.foreground
            else -> tokens.mutedForeground
        }

    Column(modifier = modifier) {
        if (label.isNotEmpty()) {
            CompositionLocalProvider(
                LocalTextStyle provides
                    typography.sm.copy(color = tokens.foreground, fontWeight = FontWeight.Medium)
            ) {
                Text(text = label, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            Spacer(modifier = Modifier.height(spacing.s1_5))
        }

        Box {
            Row(
                modifier =
                    Modifier
                        .fillMaxWidth()
                        .defaultMinSize(minHeight = SelectMinHeight)
                        .clip(shape)
                        .border(
                            width = if (expanded) ExpandedSelectBorderWidth else SelectBorderWidth,
                            color = borderColor,
                            shape = shape,
                        )
                        .background(color = tokens.muted)
                        .then(
                            if (enabled) {
                                Modifier.clickable { onExpandedChange(!expanded) }
                            } else {
                                Modifier
                            }
                        )
                        .padding(horizontal = spacing.s4, vertical = spacing.s2_5),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Box(modifier = Modifier.weight(1f)) {
                    Text(
                        text = displayText,
                        style = typography.sm.copy(color = displayColor),
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
                Icon(
                    painter = painterResource(ChevronDownGlyph),
                    contentDescription = null,
                    tint = tokens.mutedForeground,
                    modifier = Modifier.size(spacing.s4),
                )
            }

            DropdownMenu(
                expanded = expanded,
                onDismissRequest = { onExpandedChange(false) },
            ) {
                options.forEach { option: T ->
                    DropdownMenuItem(
                        text = { Text(optionLabel(option)) },
                        onClick = {
                            onValueChange(option)
                            onExpandedChange(false)
                        },
                        selected = option == value,
                    )
                }
            }
        }

        if (!supportingText.isNullOrEmpty()) {
            Spacer(modifier = Modifier.height(spacing.s1_5))
            CompositionLocalProvider(
                LocalTextStyle provides typography.xs.copy(color = tokens.mutedForeground)
            ) {
                Text(text = supportingText, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
        }
    }
}

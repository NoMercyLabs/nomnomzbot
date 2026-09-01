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
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.material3.LocalTextStyle
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.Spacing
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens
import bot.nomnomz.dashboard.core.designsystem.theme.Typography

// 1dp border stroke — not a layout spacing value.
private val FieldBorderWidth: Dp = 1.dp
private val FocusedFieldBorderWidth: Dp = 2.dp
private const val InputTransitionMillis: Int = 150

// shadcn's three field heights — a layout constraint (like Button's CompactButtonHeight), not a
// spacing-scale token.
private val SmInputHeight: Dp = 36.dp
private val DefaultInputHeight: Dp = 44.dp
private val LgInputHeight: Dp = 52.dp

/** shadcn Input sizes (frontend-design-system.md §4, catalogue row). */
enum class InputSize {
    Sm,
    Default,
    Lg,
}

private fun inputHeight(size: InputSize): Dp =
    when (size) {
        InputSize.Sm -> SmInputHeight
        InputSize.Default -> DefaultInputHeight
        InputSize.Lg -> LgInputHeight
    }

/**
 * shadcn/ui Input ported to Compose (frontend-design-system.md §4, catalogue row).
 *
 * The single-line sibling of [Textarea] (shadcn Textarea): Foundation-based ([BasicTextField]),
 * label above the field, token-driven border that responds to focus ([Tokens.ring]) and error
 * ([Tokens.destructive]), sized via [InputSize] (sm · default · lg per shadcn). `invalid` is
 * driven by the field (`isError`), never by internal validation.
 *
 * @param placeholder optional ghost text shown inside the field when [value] is empty.
 * @param supportingText optional help text shown below the field when there is no active error.
 */
@Composable
fun Input(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    modifier: Modifier = Modifier,
    size: InputSize = InputSize.Default,
    enabled: Boolean = true,
    isError: Boolean = false,
    errorText: String? = null,
    placeholder: String? = null,
    supportingText: String? = null,
) {
    val tokens: Tokens = LocalTokens.current
    val spacing: Spacing = LocalSpacing.current
    val typography: Typography = LocalTypography.current

    val interactionSource: MutableInteractionSource = remember { MutableInteractionSource() }
    val focused: Boolean by interactionSource.collectIsFocusedAsState()

    val targetBorderColor: Color =
        when {
            isError -> tokens.destructive
            focused -> tokens.ring
            !enabled -> tokens.border.copy(alpha = 0.5f)
            else -> tokens.border
        }
    val borderColor: Color by animateColorAsState(
        targetValue = targetBorderColor,
        animationSpec = tween(InputTransitionMillis),
        label = "inputBorder",
    )

    val textColor: Color = if (enabled) tokens.foreground else tokens.mutedForeground
    val shape: RoundedCornerShape = RoundedCornerShape(tokens.radius.sm)
    val fieldTextStyle = typography.sm.copy(color = textColor)

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

        BasicTextField(
            value = value,
            onValueChange = onValueChange,
            enabled = enabled,
            singleLine = true,
            textStyle = fieldTextStyle,
            cursorBrush = SolidColor(tokens.primary),
            interactionSource = interactionSource,
            modifier = Modifier.fillMaxWidth().height(inputHeight(size)),
            decorationBox = { innerTextField ->
                Box(
                    modifier =
                        Modifier
                            .fillMaxWidth()
                            .border(
                                width = if (focused || isError) FocusedFieldBorderWidth else FieldBorderWidth,
                                color = borderColor,
                                shape = shape,
                            )
                            .clip(shape)
                            .background(color = tokens.muted)
                            .padding(horizontal = spacing.s4),
                    contentAlignment = Alignment.CenterStart,
                ) {
                    if (value.isEmpty() && placeholder != null) {
                        CompositionLocalProvider(
                            LocalTextStyle provides typography.sm.copy(color = tokens.mutedForeground)
                        ) {
                            Text(text = placeholder)
                        }
                    }
                    innerTextField()
                }
            },
        )

        val subText: String? =
            when {
                isError && !errorText.isNullOrEmpty() -> errorText
                !supportingText.isNullOrEmpty() -> supportingText
                else -> null
            }
        if (subText != null) {
            Spacer(modifier = Modifier.height(spacing.s1_5))
            CompositionLocalProvider(
                LocalTextStyle provides
                    typography.xs.copy(
                        color = if (isError) tokens.destructive else tokens.mutedForeground
                    )
            ) {
                Text(text = subText, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
        }
    }
}

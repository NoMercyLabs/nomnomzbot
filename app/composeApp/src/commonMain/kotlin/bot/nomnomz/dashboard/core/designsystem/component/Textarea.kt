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
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
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
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.text.font.FontFamily
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

/**
 * shadcn/ui Textarea ported to Compose (frontend-design-system.md §4, catalogue row).
 *
 * The multi-line sibling of [AppTextField] (shadcn Input): Foundation-based ([BasicTextField]),
 * label above the field, token-driven border that responds to focus ([Tokens.ring]) and error
 * ([Tokens.destructive]). [minLines]/[maxLines] size the field to content; pass [fillHeight] with a
 * height-bounded [modifier] (e.g. `weight(1f)`) for an editor that fills its container. [monospace]
 * switches the input to a monospace family for code/JSON.
 *
 * @param placeholder optional ghost text shown inside the field when [value] is empty.
 * @param supportingText optional help text shown below the field when there is no active error.
 */
@Composable
fun Textarea(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    isError: Boolean = false,
    errorText: String? = null,
    placeholder: String? = null,
    supportingText: String? = null,
    minLines: Int = 3,
    maxLines: Int = Int.MAX_VALUE,
    monospace: Boolean = false,
    fillHeight: Boolean = false,
) {
    val tokens: Tokens = LocalTokens.current
    val spacing: Spacing = LocalSpacing.current
    val typography: Typography = LocalTypography.current

    val interactionSource: MutableInteractionSource = remember { MutableInteractionSource() }
    val focused: Boolean by interactionSource.collectIsFocusedAsState()

    val borderColor: Color =
        when {
            isError -> tokens.destructive
            focused -> tokens.ring
            !enabled -> tokens.border.copy(alpha = 0.5f)
            else -> tokens.border
        }

    val textColor: Color = if (enabled) tokens.foreground else tokens.mutedForeground
    val shape: RoundedCornerShape = RoundedCornerShape(tokens.radius.md)
    val fieldTextStyle =
        typography.sm.copy(
            color = textColor,
            fontFamily = if (monospace) FontFamily.Monospace else null,
        )

    Column(modifier = modifier) {
        if (label.isNotEmpty()) {
            CompositionLocalProvider(
                LocalTextStyle provides typography.sm.copy(color = tokens.foreground)
            ) {
                Text(text = label, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            Spacer(modifier = Modifier.height(spacing.s1_5))
        }

        // The field container — border lives on the decoration Box; the BasicTextField fills it.
        // fillHeight lets an editor grab the Column's remaining height (caller supplies the bound).
        val fieldSlotModifier: Modifier =
            if (fillHeight) Modifier.fillMaxWidth().weight(1f) else Modifier.fillMaxWidth()

        BasicTextField(
            value = value,
            onValueChange = onValueChange,
            enabled = enabled,
            singleLine = false,
            minLines = minLines,
            maxLines = maxLines,
            textStyle = fieldTextStyle,
            cursorBrush = SolidColor(tokens.primary),
            interactionSource = interactionSource,
            modifier = fieldSlotModifier,
            decorationBox = { innerTextField ->
                Box(
                    modifier =
                        Modifier
                            .then(if (fillHeight) Modifier.fillMaxHeight() else Modifier)
                            .fillMaxWidth()
                            .border(width = FieldBorderWidth, color = borderColor, shape = shape)
                            .clip(shape)
                            .background(color = tokens.background)
                            .padding(horizontal = spacing.s3, vertical = spacing.s2),
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
            Spacer(modifier = Modifier.height(spacing.s1))
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

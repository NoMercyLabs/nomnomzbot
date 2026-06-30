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
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.material3.LocalTextStyle
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextFieldColors
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.ui.text.input.VisualTransformation
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
 * shadcn/ui Input ported to Compose (frontend-design-system.md §4, catalogue row).
 *
 * Foundation-based (no Material floating label, no bottom-line animation). The label sits
 * ABOVE the field in a Column — the shadcn/new-york convention. Border color responds to focus
 * (ring token) and error state (destructive token).
 *
 * @param placeholder optional ghost text shown inside the field when [value] is empty.
 * @param supportingText optional help text shown below the field when there is no active error.
 * @param trailingIcon optional icon slot at the trailing edge of the field.
 * @param visualTransformation controls input masking — pass [PasswordVisualTransformation].
 */
@Composable
fun AppTextField(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    isError: Boolean = false,
    errorText: String? = null,
    placeholder: String? = null,
    supportingText: String? = null,
    visualTransformation: VisualTransformation = VisualTransformation.None,
    keyboardOptions: KeyboardOptions = KeyboardOptions.Default,
    keyboardActions: KeyboardActions = KeyboardActions.Default,
    trailingIcon: @Composable (() -> Unit)? = null,
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

    val textColor: Color =
        if (enabled) tokens.foreground else tokens.mutedForeground

    val shape: RoundedCornerShape = RoundedCornerShape(tokens.radius.md)

    Column(modifier = modifier) {
        // Label above field — shadcn convention (no floating label inside the border)
        if (label.isNotEmpty()) {
            CompositionLocalProvider(LocalTextStyle provides typography.sm.copy(color = tokens.foreground)) {
                Text(
                    text = label,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            Spacer(modifier = Modifier.height(spacing.s1_5))
        }

        // Field container — border lives on this Box; BasicTextField fills it
        BasicTextField(
            value = value,
            onValueChange = onValueChange,
            enabled = enabled,
            singleLine = true,
            textStyle = typography.sm.copy(color = textColor),
            cursorBrush = SolidColor(tokens.primary),
            visualTransformation = visualTransformation,
            keyboardOptions = keyboardOptions,
            keyboardActions = keyboardActions,
            interactionSource = interactionSource,
            modifier = Modifier.fillMaxWidth(),
            decorationBox = { innerTextField ->
                Row(
                    modifier =
                        Modifier
                            .fillMaxWidth()
                            .border(width = FieldBorderWidth, color = borderColor, shape = shape)
                            .clip(shape)
                            .background(color = tokens.background)
                            .padding(horizontal = spacing.s3, vertical = spacing.s2),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Box(modifier = Modifier.weight(1f)) {
                        // Placeholder shown when the field is empty
                        if (value.isEmpty() && placeholder != null) {
                            CompositionLocalProvider(
                                LocalTextStyle provides typography.sm.copy(color = tokens.mutedForeground)
                            ) {
                                Text(
                                    text = placeholder,
                                    maxLines = 1,
                                    overflow = TextOverflow.Ellipsis,
                                )
                            }
                        }
                        innerTextField()
                    }
                    if (trailingIcon != null) {
                        trailingIcon()
                    }
                }
            },
        )

        // Error / supporting text beneath the field
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

/**
 * The shared text-field colour set for Material3 [OutlinedTextField] call sites that have not yet
 * migrated to [AppTextField]. Every slot is a design token so the field reads on-theme in light +
 * dark. Migrate callers to [AppTextField] instead of adding new usages of this function.
 */
@Composable
fun appFieldColors(): TextFieldColors {
    val tokens: Tokens = LocalTokens.current
    return OutlinedTextFieldDefaults.colors(
        focusedTextColor = tokens.cardForeground,
        unfocusedTextColor = tokens.cardForeground,
        disabledTextColor = tokens.mutedForeground,
        focusedBorderColor = tokens.ring,
        unfocusedBorderColor = tokens.border,
        disabledBorderColor = tokens.border,
        errorBorderColor = tokens.destructive,
        focusedLabelColor = tokens.mutedForeground,
        unfocusedLabelColor = tokens.mutedForeground,
        disabledLabelColor = tokens.mutedForeground,
        errorLabelColor = tokens.destructive,
        cursorColor = tokens.primary,
    )
}

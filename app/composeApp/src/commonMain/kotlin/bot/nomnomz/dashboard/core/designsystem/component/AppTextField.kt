// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.component

import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.hoverable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.interaction.collectIsHoveredAsState
import androidx.compose.foundation.layout.Arrangement
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
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.LocalContentColor
import androidx.compose.material3.LocalTextStyle
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.input.pointer.PointerIcon
import androidx.compose.ui.input.pointer.pointerHoverIcon
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import bot.nomnomz.dashboard.core.designsystem.theme.ControlPalette
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.info_square
import org.jetbrains.compose.resources.painterResource

private val FieldBorderWidth: Dp = 1.dp
private val FocusedFieldBorderWidth: Dp = 3.dp
private val FieldHeight: Dp = 54.dp
private val FieldRadius: Dp = 16.dp
private val FieldHorizontalInset: Dp = 16.dp
private val FieldTrailingInset: Dp = 6.dp
private val FieldGap: Dp = 10.dp
private val LabelGap: Dp = 6.dp
private val ActionHeight: Dp = 42.dp
private val ActionRadius: Dp = 12.dp
private const val FieldTransitionMillis: Int = 120

/**
 * Figma node 308:1582. Optional slots keep the existing app API source-compatible while exposing
 * the frame's leading icon, label-info icon, and inset action treatment.
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
    leadingIcon: @Composable (() -> Unit)? = null,
    showLabelInfoIcon: Boolean = false,
    actionLabel: String? = null,
    onActionClick: (() -> Unit)? = null,
) {
    val typography = LocalTypography.current
    val interactionSource = remember { MutableInteractionSource() }
    val focused: Boolean by interactionSource.collectIsFocusedAsState()
    val hovered: Boolean by interactionSource.collectIsHoveredAsState()
    val shape = RoundedCornerShape(FieldRadius)
    val targetBackground = if (hovered && enabled && !focused) ControlPalette.SurfaceRaised else ControlPalette.Surface
    val targetBorder =
        when {
            isError -> ControlPalette.DestructiveContent
            focused -> ControlPalette.Focus
            hovered && enabled -> ControlPalette.BorderHover
            else -> ControlPalette.Border
        }
    val background: Color by
        animateColorAsState(targetBackground, tween(FieldTransitionMillis), label = "inputBackground")
    val border: Color by
        animateColorAsState(targetBorder, tween(FieldTransitionMillis), label = "inputBorder")
    val inputColor = if (enabled) ControlPalette.White else ControlPalette.White.copy(alpha = 0.32f)
    val hasTrailing = actionLabel != null || trailingIcon != null

    Column(modifier = modifier) {
        if (label.isNotEmpty()) {
            Row(
                modifier = Modifier.fillMaxWidth().padding(horizontal = FieldHorizontalInset),
                horizontalArrangement = Arrangement.spacedBy(4.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    text = label,
                    style =
                        typography.base.copy(
                            color = ControlPalette.White,
                            fontSize = 16.sp,
                            lineHeight = 20.sp,
                            letterSpacing = 0.48.sp,
                        ),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                if (showLabelInfoIcon) {
                    Image(
                        painter = painterResource(Res.drawable.info_square),
                        contentDescription = null,
                        modifier = Modifier.size(16.dp),
                    )
                }
            }
            Spacer(Modifier.height(LabelGap))
        }

        BasicTextField(
            value = value,
            onValueChange = onValueChange,
            enabled = enabled,
            singleLine = true,
            textStyle =
                typography.base.copy(
                    color = inputColor,
                    fontSize = 16.sp,
                    lineHeight = 22.sp,
                    letterSpacing = (-0.0688).sp,
                ),
            cursorBrush = SolidColor(ControlPalette.White),
            visualTransformation = visualTransformation,
            keyboardOptions = keyboardOptions,
            keyboardActions = keyboardActions,
            interactionSource = interactionSource,
            modifier =
                Modifier
                    .fillMaxWidth()
                    .hoverable(interactionSource, enabled = enabled),
            decorationBox = { innerTextField ->
                Row(
                    modifier =
                        Modifier
                            .fillMaxWidth()
                            .defaultMinSize(minHeight = FieldHeight)
                            .border(
                                width = if (focused || isError) FocusedFieldBorderWidth else FieldBorderWidth,
                                color = border,
                                shape = shape,
                            )
                            .clip(shape)
                            .background(background)
                            .then(if (!enabled) Modifier.alpha(0.48f) else Modifier)
                            .padding(
                                start = FieldHorizontalInset,
                                end = if (hasTrailing) FieldTrailingInset else FieldHorizontalInset,
                                top = 2.dp,
                                bottom = 2.dp,
                            ),
                    horizontalArrangement = Arrangement.spacedBy(FieldGap),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    if (leadingIcon != null) {
                        CompositionLocalProvider(LocalContentColor provides ControlPalette.White.copy(alpha = 0.56f)) {
                            Box(
                                modifier =
                                    Modifier
                                        .size(24.dp)
                                        .clip(RoundedCornerShape(16.dp))
                                        .background(ControlPalette.White.copy(alpha = 0.10f)),
                                contentAlignment = Alignment.Center,
                            ) {
                                leadingIcon()
                            }
                        }
                    }
                    Box(modifier = Modifier.weight(1f)) {
                        if (value.isEmpty() && placeholder != null) {
                            Text(
                                text = placeholder,
                                style =
                                    typography.base.copy(
                                        color =
                                            ControlPalette.White.copy(
                                                alpha = if (hovered && enabled) 0.56f else 0.32f
                                            ),
                                        fontSize = 16.sp,
                                        lineHeight = 22.sp,
                                        letterSpacing = (-0.0688).sp,
                                    ),
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis,
                            )
                        }
                        innerTextField()
                    }
                    when {
                        actionLabel != null ->
                            InputFieldAction(
                                label = actionLabel,
                                onClick = onActionClick,
                                enabled = enabled,
                            )
                        trailingIcon != null -> trailingIcon()
                    }
                }
            },
        )

        val subText =
            when {
                isError && !errorText.isNullOrEmpty() -> errorText
                !supportingText.isNullOrEmpty() -> supportingText
                else -> null
            }
        if (subText != null) {
            Spacer(Modifier.height(LabelGap))
            Text(
                text = subText,
                style =
                    typography.xs.copy(
                        color = if (isError) ControlPalette.DestructiveContent else ControlPalette.Helper,
                        fontSize = 12.sp,
                        letterSpacing = 0.36.sp,
                    ),
                modifier = Modifier.fillMaxWidth().padding(horizontal = FieldHorizontalInset),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
}

@Composable
internal fun InputFieldAction(label: String, onClick: (() -> Unit)?, enabled: Boolean) {
    val typography = LocalTypography.current
    val interactionSource = remember { MutableInteractionSource() }
    val hovered: Boolean by interactionSource.collectIsHoveredAsState()
    val interactive = enabled && onClick != null
    val fill: Color by
        animateColorAsState(
            if (hovered && interactive) ControlPalette.SurfaceHover else ControlPalette.SurfaceRaised,
            tween(FieldTransitionMillis),
            label = "inputAction",
        )
    val content = if (hovered && interactive) ControlPalette.White else ControlPalette.White.copy(alpha = 0.70f)
    val shape = RoundedCornerShape(ActionRadius)
    Box(
        modifier =
            Modifier
                .height(ActionHeight)
                .clip(shape)
                .background(fill)
                .hoverable(interactionSource, enabled = interactive)
                .clickable(
                    interactionSource = interactionSource,
                    indication = null,
                    enabled = interactive,
                    onClick = onClick ?: {},
                )
                .pointerHoverIcon(if (interactive) PointerIcon.Hand else PointerIcon.Default)
                .padding(horizontal = 22.dp),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = label,
            style =
                typography.base.copy(
                    color = content,
                    fontWeight = FontWeight.SemiBold,
                    fontSize = 16.sp,
                    lineHeight = 21.sp,
                    letterSpacing = 0.48.sp,
                ),
            maxLines = 1,
        )
    }
}

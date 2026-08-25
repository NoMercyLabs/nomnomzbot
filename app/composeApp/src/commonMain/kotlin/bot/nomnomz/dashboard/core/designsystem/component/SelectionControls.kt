// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.component

import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.focusable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.interaction.collectIsHoveredAsState
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.toggleable
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.PointerIcon
import androidx.compose.ui.input.pointer.pointerHoverIcon
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.theme.ControlPalette
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.checkbox_check
import org.jetbrains.compose.resources.painterResource

private val SelectionTargetSize: Dp = 44.dp
private val SelectionIndicatorSize: Dp = 26.dp
private val CheckAssetWidth: Dp = 20.dp
private val CheckAssetHeight: Dp = 16.dp
private val SelectionBorderWidth: Dp = 2.dp
private val SelectedRadioBorderWidth: Dp = 6.dp
private val FocusBorderWidth: Dp = 3.dp
private val CheckboxRadius: Dp = 6.dp

/** Figma node 235:1466. The checked mark is the exact exported vector from the frame. */
@Composable
fun Checkbox(
    checked: Boolean,
    onCheckedChange: ((Boolean) -> Unit)?,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
) {
    val interactionSource = remember { MutableInteractionSource() }
    val hovered: Boolean by interactionSource.collectIsHoveredAsState()
    val focused: Boolean by interactionSource.collectIsFocusedAsState()
    val interactive = onCheckedChange != null
    SelectionTarget(
        modifier = modifier,
        enabled = enabled,
        interactionModifier =
            if (interactive) {
                Modifier
                    .focusable(enabled = enabled, interactionSource = interactionSource)
                    .toggleable(
                        value = checked,
                        interactionSource = interactionSource,
                        indication = null,
                        enabled = enabled,
                        role = Role.Checkbox,
                        onValueChange = onCheckedChange!!,
                    )
            } else {
                Modifier
            },
    ) {
        val shape = RoundedCornerShape(CheckboxRadius)
        Box(
            modifier =
                Modifier
                    .size(SelectionIndicatorSize)
                    .clip(shape)
                    .then(
                        if (checked) {
                            Modifier.background(
                                Brush.verticalGradient(
                                    listOf(ControlPalette.White, ControlPalette.LilacWhite)
                                )
                            )
                        } else {
                            Modifier
                                .background(Color.Transparent)
                                .border(
                                    width = if (focused) FocusBorderWidth else SelectionBorderWidth,
                                    color = selectionOutline(hovered, focused, enabled),
                                    shape = shape,
                                )
                        }
                    ),
            contentAlignment = Alignment.Center,
        ) {
            if (checked) {
                Image(
                    painter = painterResource(Res.drawable.checkbox_check),
                    contentDescription = null,
                    modifier = Modifier.size(CheckAssetWidth, CheckAssetHeight),
                )
            }
        }
    }
}

/** Figma node 235:1461 — selected is a dark center created by a 6px white ring. */
@Composable
fun RadioButton(
    selected: Boolean,
    onClick: (() -> Unit)?,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
) {
    val interactionSource = remember { MutableInteractionSource() }
    val hovered: Boolean by interactionSource.collectIsHoveredAsState()
    val focused: Boolean by interactionSource.collectIsFocusedAsState()
    SelectionTarget(
        modifier = modifier,
        enabled = enabled,
        interactionModifier =
            if (onClick != null) {
                Modifier
                    .focusable(enabled = enabled, interactionSource = interactionSource)
                    .selectable(
                        selected = selected,
                        interactionSource = interactionSource,
                        indication = null,
                        enabled = enabled,
                        role = Role.RadioButton,
                        onClick = onClick,
                    )
            } else {
                Modifier
            },
    ) {
        Box(
            modifier =
                Modifier
                    .size(SelectionIndicatorSize)
                    .clip(CircleShape)
                    .background(if (selected) ControlPalette.Ink else Color.Transparent)
                    .border(
                        width =
                            when {
                                focused -> FocusBorderWidth
                                selected -> SelectedRadioBorderWidth
                                else -> SelectionBorderWidth
                            },
                        color =
                            when {
                                focused -> ControlPalette.Focus
                                selected -> ControlPalette.White
                                else -> selectionOutline(hovered, focused, enabled)
                            },
                        shape = CircleShape,
                    )
        )
    }
}

@Composable
private fun SelectionTarget(
    modifier: Modifier,
    enabled: Boolean,
    interactionModifier: Modifier,
    content: @Composable () -> Unit,
) {
    Box(
        modifier =
            modifier
                .defaultMinSize(minWidth = SelectionTargetSize, minHeight = SelectionTargetSize)
                .then(if (!enabled) Modifier.alpha(0.48f) else Modifier)
                .then(interactionModifier)
                .pointerHoverIcon(if (enabled) PointerIcon.Hand else PointerIcon.Default),
        contentAlignment = Alignment.Center,
    ) {
        content()
    }
}

internal fun selectionOutline(
    hovered: Boolean,
    focused: Boolean,
    enabled: Boolean,
): Color =
    when {
        !enabled -> ControlPalette.InactiveOutline
        focused -> ControlPalette.Focus
        hovered -> ControlPalette.InactiveOutline
        else -> ControlPalette.InactiveOutline
    }

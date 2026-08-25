// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.component

import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.focusable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.selection.toggleable
import androidx.compose.foundation.shape.CircleShape
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

private val TrackWidth: Dp = 56.dp
private val TrackHeight: Dp = 32.dp
private val ThumbWidth: Dp = 34.dp
private val ThumbHeight: Dp = 26.dp
private val ThumbInset: Dp = 3.dp
private val TrackBorderWidth: Dp = 1.5.dp
private val FocusBorderWidth: Dp = 2.dp

/** Figma node 235:1454 — 56×32 pill switch with a wide 34×26 thumb. */
@Composable
fun Switch(
    checked: Boolean,
    onCheckedChange: ((Boolean) -> Unit)?,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
) {
    val interactionSource = remember { MutableInteractionSource() }
    val focused: Boolean by interactionSource.collectIsFocusedAsState()
    val thumbOffset: Dp by
        animateDpAsState(
            targetValue = if (checked) TrackWidth - ThumbWidth - ThumbInset else ThumbInset,
            label = "switchThumbOffset",
        )
    val outline: Color by
        animateColorAsState(
            targetValue =
                when {
                    focused -> ControlPalette.Focus
                    checked -> Color.Transparent
                    else -> ControlPalette.InactiveOutline
                },
            label = "switchOutline",
        )
    val controlModifier =
        if (onCheckedChange != null) {
            Modifier
                .focusable(enabled = enabled, interactionSource = interactionSource)
                .toggleable(
                    value = checked,
                    interactionSource = interactionSource,
                    indication = null,
                    enabled = enabled,
                    role = Role.Switch,
                    onValueChange = onCheckedChange,
                )
                .pointerHoverIcon(if (enabled) PointerIcon.Hand else PointerIcon.Default)
        } else {
            Modifier
        }

    Box(
        modifier =
            modifier
                .then(if (!enabled) Modifier.alpha(0.48f) else Modifier)
                .size(TrackWidth, TrackHeight)
                .then(controlModifier)
                .clip(CircleShape)
                .then(
                    if (checked) {
                        Modifier.background(
                            Brush.verticalGradient(
                                listOf(ControlPalette.White, ControlPalette.LilacWhite)
                            )
                        )
                    } else {
                        Modifier.background(Color.Transparent)
                    }
                )
                .border(
                    width = if (focused) FocusBorderWidth else TrackBorderWidth,
                    color = outline,
                    shape = CircleShape,
                ),
        contentAlignment = Alignment.CenterStart,
    ) {
        Box(
            modifier =
                Modifier
                    .offset(x = thumbOffset)
                    .size(ThumbWidth, ThumbHeight)
                    .clip(CircleShape)
                    .background(
                        if (checked) {
                            Brush.verticalGradient(
                                listOf(ControlPalette.Ink, ControlPalette.Ink.copy(alpha = 0.8f))
                            )
                        } else {
                            Brush.verticalGradient(
                                listOf(ControlPalette.LilacWhite, ControlPalette.White.copy(alpha = 0.8f))
                            )
                        }
                    ),
        )
    }
}

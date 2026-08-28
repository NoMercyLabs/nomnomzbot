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
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.waitForUpOrCancellation
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsHoveredAsState
import androidx.compose.foundation.hoverable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.IntRect
import androidx.compose.ui.unit.IntSize
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Popup
import androidx.compose.ui.window.PopupPositionProvider
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens
import bot.nomnomz.dashboard.core.designsystem.theme.Typography

// Delay before the bubble appears on hover — matches shadcn's ~700ms open delay closely enough to
// avoid flicker while sweeping the pointer across a toolbar of icon buttons.
private const val TooltipDelayMillis: Long = 400L

// Gap between the anchor and the bubble, in px (device-independent enough for a 1px-ish offset).
private const val TooltipGapPx: Int = 4
private val TooltipElevation: Dp = 8.dp

/**
 * shadcn/ui Tooltip ported to Compose (frontend-design-system.md §4, catalogue row — Foundation
 * `Popup`).
 *
 * Hover-triggered: wraps the anchor in [content], detects pointer hover via a Foundation
 * `InteractionSource`, and after a short delay shows the inverse-surface bubble centered above the
 * anchor in a `Popup`. No Material dependency, so it renders and triggers reliably on desktop and
 * web. The [text] is also the accessible name callers should set on the anchor itself.
 */
@Composable
fun Tooltip(
    text: String,
    modifier: Modifier = Modifier,
    content: @Composable () -> Unit,
) {
    val tokens: Tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography: Typography = LocalTypography.current

    val interactionSource: MutableInteractionSource = remember { MutableInteractionSource() }
    val hovered: Boolean by interactionSource.collectIsHoveredAsState()
    var focused: Boolean by remember { mutableStateOf(false) }
    // True while the anchor is pressed/held (CSS :active) — cleared on release. A click on the anchor
    // also LEAVES it focused afterward (like a native button), so this additionally marks that the
    // upcoming focus was pointer-acquired rather than keyboard (Tab) reached: it starts true on the
    // down event, before onFocusChanged below observes the resulting focus change.
    var pointerDown: Boolean by remember { mutableStateOf(false) }
    // Cleared only when focus is fully lost, so a click-acquired focus stays "not focus-visible" for as
    // long as it's held, exactly like CSS :focus-visible on a mouse-focused button.
    var pointerAcquiredFocus: Boolean by remember { mutableStateOf(false) }
    var visible: Boolean by remember { mutableStateOf(false) }
    val focusVisible: Boolean = focused && !pointerAcquiredFocus

    // Keyboard (focus-visible) opens immediately; pointer hover retains the deliberate open delay.
    // Active (pointerDown) always wins and hides it right away — a tooltip must never linger over a
    // button the user is actively pressing/clicking.
    LaunchedEffect(hovered, focusVisible, pointerDown) {
        if (pointerDown) {
            visible = false
        } else if (focusVisible) {
            visible = true
        } else if (hovered) {
            kotlinx.coroutines.delay(TooltipDelayMillis)
            visible = hovered && !pointerDown
        } else {
            visible = false
        }
    }

    Box(
        modifier =
            modifier
                .onFocusChanged { state ->
                    focused = state.hasFocus
                    if (!state.hasFocus) pointerAcquiredFocus = false
                }
                .hoverable(interactionSource)
                // Initial pass, never consumed: observes the anchor's own press/release without
                // interfering with its click handling — just tracks active/pointer-focus state above.
                .pointerInput(Unit) {
                    awaitEachGesture {
                        awaitFirstDown(requireUnconsumed = false, pass = PointerEventPass.Initial)
                        pointerDown = true
                        pointerAcquiredFocus = true
                        waitForUpOrCancellation(pass = PointerEventPass.Initial)
                        pointerDown = false
                    }
                }
    ) {
        content()
        if (visible) {
            Popup(popupPositionProvider = AboveAnchorPositionProvider) {
                Box(
                    modifier =
                        Modifier
                            .shadow(TooltipElevation, RoundedCornerShape(tokens.radius.sm))
                            .clip(RoundedCornerShape(tokens.radius.sm))
                            .background(tokens.foreground)
                            .padding(horizontal = spacing.s3, vertical = spacing.s1_5),
                ) {
                    Text(text = text, style = typography.xs, color = tokens.background)
                }
            }
        }
    }
}

// Centers the bubble horizontally on the anchor and places it just above, clamped into the window.
private val AboveAnchorPositionProvider: PopupPositionProvider =
    object : PopupPositionProvider {
        override fun calculatePosition(
            anchorBounds: IntRect,
            windowSize: IntSize,
            layoutDirection: LayoutDirection,
            popupContentSize: IntSize,
        ): IntOffset {
            val x: Int =
                (anchorBounds.left + (anchorBounds.width - popupContentSize.width) / 2)
                    .coerceIn(0, (windowSize.width - popupContentSize.width).coerceAtLeast(0))
            // Prefer above the anchor; if it would clip the top, fall below instead.
            val above: Int = anchorBounds.top - popupContentSize.height - TooltipGapPx
            val y: Int = if (above >= 0) above else anchorBounds.bottom + TooltipGapPx
            return IntOffset(x, y)
        }
    }

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
private const val TooltipDelayMillis: Long = 500L

// Gap between the anchor and the bubble, in px (device-independent enough for a 1px-ish offset).
private const val TooltipGapPx: Int = 8

/**
 * shadcn/ui Tooltip ported to Compose (frontend-design-system.md §4, catalogue row — Foundation
 * `Popup`).
 *
 * Hover-triggered: wraps the anchor in [content], detects pointer hover via a Foundation
 * `InteractionSource`, and after a short delay shows a [Tokens.popover] bubble centered above the
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
    var visible: Boolean by remember { mutableStateOf(false) }

    // Debounce: only show after the pointer has rested on the anchor for the open delay.
    LaunchedEffect(hovered) {
        if (hovered) {
            kotlinx.coroutines.delay(TooltipDelayMillis)
            visible = true
        } else {
            visible = false
        }
    }

    Box(modifier = modifier.hoverable(interactionSource)) {
        content()
        if (visible) {
            Popup(popupPositionProvider = AboveAnchorPositionProvider) {
                Box(
                    modifier =
                        Modifier
                            .clip(RoundedCornerShape(tokens.radius.md))
                            .background(tokens.popover)
                            .border(1.dp, tokens.border, RoundedCornerShape(tokens.radius.md))
                            .padding(horizontal = spacing.s2, vertical = spacing.s1),
                ) {
                    Text(text = text, style = typography.xs, color = tokens.popoverForeground)
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

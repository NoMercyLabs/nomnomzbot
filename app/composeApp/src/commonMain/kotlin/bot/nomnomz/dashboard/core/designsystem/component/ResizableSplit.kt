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
import androidx.compose.foundation.gestures.Orientation
import androidx.compose.foundation.gestures.draggable
import androidx.compose.foundation.gestures.rememberDraggableState
import androidx.compose.foundation.hoverable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsHoveredAsState
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.width
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.input.pointer.PointerIcon
import androidx.compose.ui.input.pointer.pointerHoverIcon
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens

// 1dp visual divider stroke — not a layout spacing value.
private val DividerWidth: Dp = 1.dp

// The draggable handle's hit area (wider than the 1dp line so it is easy to grab), and the minimum width each
// pane keeps so neither collapses to nothing. Interaction affordances, not design-token spacing.
private val HandleHitWidth: Dp = 8.dp
private val MinPaneWidth: Dp = 120.dp

/**
 * A horizontal two-pane split with a draggable divider (frontend-design-system.md §4, catalogue row —
 * `ResizableSplit`). Foundation-based: the caller supplies [left] and [right] pane content; the divider between
 * them is drag-resizable and clamped so neither pane shrinks below a usable minimum. Colours come from
 * [Tokens] only — the line is [Tokens.border], the grabbed/hovered handle is [Tokens.ring].
 *
 * Sizing is by fraction of the available width: [initialLeftFraction] seeds the split (0..1), and the component
 * owns the live fraction internally so a drag re-lays out immediately. Fills the width and height of its parent,
 * so give it a bounded container (e.g. a `Card` with `weight(1f)`).
 */
@Composable
fun ResizableSplit(
    left: @Composable () -> Unit,
    right: @Composable () -> Unit,
    modifier: Modifier = Modifier,
    initialLeftFraction: Float = 0.32f,
) {
    val tokens: Tokens = LocalTokens.current
    val density = LocalDensity.current

    val interactionSource: MutableInteractionSource = remember { MutableInteractionSource() }
    val dragged: Boolean by interactionSource.collectIsHoveredAsState()

    BoxWithConstraints(modifier = modifier.fillMaxSize()) {
        val totalWidth: Dp = maxWidth
        var leftWidth: Dp by remember(totalWidth) {
            mutableStateOf((totalWidth * initialLeftFraction).coerceIn(MinPaneWidth, totalWidth - MinPaneWidth))
        }

        val draggableState =
            rememberDraggableState { deltaPx ->
                val deltaDp: Dp = with(density) { deltaPx.toDp() }
                leftWidth = (leftWidth + deltaDp).coerceIn(MinPaneWidth, totalWidth - MinPaneWidth)
            }

        Row(modifier = Modifier.fillMaxSize()) {
            Box(modifier = Modifier.width(leftWidth).fillMaxHeight()) { left() }

            // The divider: a 1dp token line inside a wider, draggable hit area with a resize cursor.
            Box(
                modifier =
                    Modifier
                        .width(HandleHitWidth)
                        .fillMaxHeight()
                        .pointerHoverIcon(PointerIcon.Crosshair)
                        .hoverable(interactionSource)
                        .draggable(
                            state = draggableState,
                            orientation = Orientation.Horizontal,
                            interactionSource = interactionSource,
                        ),
            ) {
                Box(
                    modifier =
                        Modifier
                            .width(DividerWidth)
                            .fillMaxHeight()
                            .background(if (dragged) tokens.ring else tokens.border)
                            .align(Alignment.Center),
                )
            }

            Box(modifier = Modifier.width(totalWidth - leftWidth - HandleHitWidth).fillMaxHeight()) { right() }
        }
    }
}

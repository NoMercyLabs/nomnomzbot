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

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideInVertically
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.width
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens

// The scrim behind the panel — a fixed 50 % black wash, not a token colour.
private val SheetScrim: Color = Color.Black.copy(alpha = 0.5f)

// Default panel extent for the horizontal sides (shadcn sheet ≈ w-3/4 sm:max-w-sm).
private val SheetSideWidth: Dp = 320.dp

/** shadcn Sheet side (frontend-design-system.md §4, catalogue row). */
enum class SheetSide {
    Top,
    Right,
    Bottom,
    Left,
}

/**
 * shadcn/ui Sheet ported to Compose (frontend-design-system.md §4, catalogue row).
 *
 * Foundation-based modal side panel: a scrimmed full-screen overlay (Compose's window `Dialog`
 * primitive) with a [Tokens.background] panel that slides in from [side]. Clicking the scrim or a
 * back-press invokes [onDismissRequest]. Renders nothing when [open] is false. Left/right panels get
 * a default width; top/bottom fill the width. Replaces Material3's `ModalNavigationDrawer` for
 * modal side surfaces.
 */
@Composable
fun Sheet(
    open: Boolean,
    onDismissRequest: () -> Unit,
    modifier: Modifier = Modifier,
    side: SheetSide = SheetSide.Right,
    content: @Composable ColumnScope.() -> Unit,
) {
    if (!open) return
    val tokens: Tokens = LocalTokens.current

    Dialog(
        onDismissRequest = onDismissRequest,
        properties = DialogProperties(usePlatformDefaultWidth = false),
    ) {
        // Trip the entrance animation on first composition of the open state.
        var visible: Boolean by remember { mutableStateOf(false) }
        LaunchedEffect(Unit) { visible = true }

        val scrimInteraction: MutableInteractionSource = remember { MutableInteractionSource() }

        Box(modifier = Modifier.fillMaxSize()) {
            // Scrim — dismiss on click.
            Box(
                modifier =
                    Modifier
                        .fillMaxSize()
                        .background(SheetScrim)
                        .clickable(
                            interactionSource = scrimInteraction,
                            indication = null,
                            onClick = onDismissRequest,
                        )
            )

            val panelModifier: Modifier =
                when (side) {
                    SheetSide.Left,
                    SheetSide.Right -> modifier.fillMaxHeight().width(SheetSideWidth)
                    SheetSide.Top,
                    SheetSide.Bottom -> modifier.fillMaxWidth()
                }

            AnimatedVisibility(
                visible = visible,
                modifier = Modifier.align(side.toAlignment()),
                enter =
                    when (side) {
                        SheetSide.Left -> slideInHorizontally { -it }
                        SheetSide.Right -> slideInHorizontally { it }
                        SheetSide.Top -> slideInVertically { -it }
                        SheetSide.Bottom -> slideInVertically { it }
                    },
            ) {
                Column(
                    modifier = panelModifier.background(tokens.background),
                    content = content,
                )
            }
        }
    }
}

private fun SheetSide.toAlignment(): Alignment =
    when (this) {
        SheetSide.Left -> Alignment.CenterStart
        SheetSide.Right -> Alignment.CenterEnd
        SheetSide.Top -> Alignment.TopCenter
        SheetSide.Bottom -> Alignment.BottomCenter
    }

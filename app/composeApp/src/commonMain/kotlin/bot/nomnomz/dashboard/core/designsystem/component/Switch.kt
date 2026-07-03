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
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.interaction.MutableInteractionSource
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
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.PointerIcon
import androidx.compose.ui.input.pointer.pointerHoverIcon
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens

// shadcn Switch geometry (h-5 w-9 track, size-4 thumb, 2px inset). Fixed control dimensions —
// not layout spacing — so they live here as named constants rather than in Space.*.
private val TrackWidth: Dp = 36.dp
private val TrackHeight: Dp = 20.dp
private val ThumbSize: Dp = 16.dp
private val ThumbInset: Dp = 2.dp
private val TrackBorderWidth: Dp = 1.dp

/**
 * shadcn/ui Switch ported to Compose (frontend-design-system.md §4, catalogue row).
 *
 * Foundation-based (no Material thumb/elevation/ripple) so it reads as shadcn, not Material: a pill
 * track that animates [Tokens.input]→[Tokens.primary] with a circular [Tokens.background] thumb that
 * slides on toggle. `toggleable` with [Role.Switch] carries the on/off state to the a11y tree. Same
 * call signature as `androidx.compose.material3.Switch`.
 */
@Composable
fun Switch(
    checked: Boolean,
    onCheckedChange: ((Boolean) -> Unit)?,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
) {
    val tokens: Tokens = LocalTokens.current

    val trackColor: Color by
        animateColorAsState(
            targetValue =
                when {
                    checked -> tokens.primary
                    else -> tokens.input
                },
            label = "switchTrack",
        )
    val thumbOffset: Dp by
        animateDpAsState(
            targetValue = if (checked) TrackWidth - ThumbSize - ThumbInset else ThumbInset,
            label = "switchThumb",
        )

    val interactionSource: MutableInteractionSource = remember { MutableInteractionSource() }

    val toggleModifier: Modifier =
        if (onCheckedChange != null)
            Modifier
                .toggleable(
                    value = checked,
                    interactionSource = interactionSource,
                    indication = null,
                    enabled = enabled,
                    role = Role.Switch,
                    onValueChange = onCheckedChange,
                )
                .pointerHoverIcon(if (enabled) PointerIcon.Hand else PointerIcon.Default)
        else Modifier

    Box(
        modifier =
            modifier
                .then(if (!enabled) Modifier.alpha(0.5f) else Modifier)
                .size(width = TrackWidth, height = TrackHeight)
                .then(toggleModifier)
                .clip(CircleShape)
                .background(trackColor)
                .border(
                    width = TrackBorderWidth,
                    color = if (checked) Color.Transparent else tokens.border,
                    shape = CircleShape,
                ),
        contentAlignment = Alignment.CenterStart,
    ) {
        Box(
            modifier =
                Modifier
                    .offset(x = thumbOffset)
                    .size(ThumbSize)
                    .clip(CircleShape)
                    .background(tokens.background),
        )
    }
}

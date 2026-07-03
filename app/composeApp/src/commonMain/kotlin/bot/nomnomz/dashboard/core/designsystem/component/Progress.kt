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
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens

// shadcn Progress track height (h-2).
private val ProgressTrackHeight: Dp = 8.dp

/**
 * shadcn/ui Progress ported to Compose (frontend-design-system.md §4, catalogue row).
 *
 * Foundation-based determinate bar: a [Tokens.primary]-over-[Tokens.secondary] track filled to
 * [value] (0f–1f). Fully rounded ends. For an indeterminate/loading state use [Spinner] instead.
 */
@Composable
fun Progress(
    value: Float,
    modifier: Modifier = Modifier,
) {
    val tokens: Tokens = LocalTokens.current
    val shape: RoundedCornerShape = RoundedCornerShape(percent = 50)
    val fraction: Float = value.coerceIn(0f, 1f)

    Box(
        modifier =
            modifier
                .fillMaxWidth()
                .height(ProgressTrackHeight)
                .clip(shape)
                .background(tokens.secondary)
    ) {
        Box(
            modifier =
                Modifier
                    .fillMaxWidth(fraction)
                    .fillMaxHeight()
                    .clip(shape)
                    .background(tokens.primary)
        )
    }
}

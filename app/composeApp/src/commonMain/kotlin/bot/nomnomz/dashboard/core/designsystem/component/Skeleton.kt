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

import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens

// shadcn's default skeleton block height (a single text-line placeholder) when the caller doesn't
// size it explicitly via modifier.
private val SkeletonDefaultHeight: Dp = 16.dp

// Shimmer pulse bounds and cadence — shadcn's `animate-pulse` (opacity 1 -> 0.5 -> 1, 2s cycle).
private const val SkeletonPulseMinAlpha: Float = 0.5f
private const val SkeletonPulseMaxAlpha: Float = 1f
private const val SkeletonPulseDurationMs: Int = 1000

/**
 * shadcn/ui Skeleton ported to Compose (frontend-design-system.md §4, catalogue row).
 *
 * Foundation-based loading placeholder: a [Tokens.muted]-filled rounded block that pulses opacity
 * between 0.5 and 1 on an infinite loop, matching shadcn's `animate-pulse`. Size it via [modifier]
 * (`fillMaxWidth().height(...)`, `size(...)`) for the shape it stands in for — text line, avatar
 * circle, card block, etc.
 */
@Composable
fun Skeleton(modifier: Modifier = Modifier) {
    val tokens: Tokens = LocalTokens.current
    val transition = rememberInfiniteTransition(label = "skeletonPulse")
    val alpha: Float by
        transition.animateFloat(
            initialValue = SkeletonPulseMaxAlpha,
            targetValue = SkeletonPulseMinAlpha,
            animationSpec =
                infiniteRepeatable(
                    animation = tween(durationMillis = SkeletonPulseDurationMs, easing = LinearEasing),
                    repeatMode = RepeatMode.Reverse,
                ),
            label = "skeletonAlpha",
        )
    val shape = RoundedCornerShape(tokens.radius.sm)
    val defaultSize: Modifier = Modifier.fillMaxWidth().height(SkeletonDefaultHeight)

    Box(
        modifier =
            defaultSize
                .then(modifier)
                .clip(shape)
                .background(tokens.muted.copy(alpha = alpha))
    )
}

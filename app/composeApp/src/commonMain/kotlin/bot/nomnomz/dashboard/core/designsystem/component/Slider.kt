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

import androidx.compose.material3.Slider as Material3Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens

/**
 * shadcn/ui Slider ported to Compose (frontend-design-system.md §4, catalogue row).
 *
 * A themed wrapper over Material3's `Slider` — the a11y-correct primitive (DS7 "M3-wrapped") —
 * recoloured to the shadcn contract: active track + thumb = [Tokens.primary], inactive track =
 * [Tokens.secondary]. Same call signature as `androidx.compose.material3.Slider`, so call sites
 * only need an import swap.
 */
@Composable
fun Slider(
    value: Float,
    onValueChange: (Float) -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    valueRange: ClosedFloatingPointRange<Float> = 0f..1f,
    steps: Int = 0,
    onValueChangeFinished: (() -> Unit)? = null,
) {
    val tokens: Tokens = LocalTokens.current
    Material3Slider(
        value = value,
        onValueChange = onValueChange,
        modifier = modifier,
        enabled = enabled,
        valueRange = valueRange,
        steps = steps,
        onValueChangeFinished = onValueChangeFinished,
        colors =
            SliderDefaults.colors(
                thumbColor = tokens.primary,
                activeTrackColor = tokens.primary,
                inactiveTrackColor = tokens.secondary,
                disabledThumbColor = tokens.primary.copy(alpha = 0.5f),
                disabledActiveTrackColor = tokens.primary.copy(alpha = 0.4f),
                disabledInactiveTrackColor = tokens.secondary.copy(alpha = 0.5f),
            ),
    )
}

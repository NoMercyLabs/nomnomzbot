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

import androidx.compose.material3.Switch as Material3Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens

/**
 * shadcn/ui Switch ported to Compose (frontend-design-system.md §4, catalogue row).
 *
 * A themed wrapper over Material3's `Switch` — the a11y-correct primitive (DS7 "M3-wrapped") —
 * recoloured to the shadcn contract: checked track = [Tokens.primary], unchecked track =
 * [Tokens.input], thumb = [Tokens.background]. Same call signature as `androidx.compose.material3.Switch`,
 * so call sites only need an import swap.
 */
@Composable
fun Switch(
    checked: Boolean,
    onCheckedChange: ((Boolean) -> Unit)?,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
) {
    val tokens: Tokens = LocalTokens.current
    Material3Switch(
        checked = checked,
        onCheckedChange = onCheckedChange,
        modifier = modifier,
        enabled = enabled,
        colors =
            SwitchDefaults.colors(
                checkedThumbColor = tokens.background,
                checkedTrackColor = tokens.primary,
                checkedBorderColor = Color.Transparent,
                uncheckedThumbColor = tokens.background,
                uncheckedTrackColor = tokens.input,
                uncheckedBorderColor = tokens.border,
                disabledCheckedThumbColor = tokens.background,
                disabledCheckedTrackColor = tokens.primary.copy(alpha = 0.5f),
                disabledUncheckedThumbColor = tokens.background,
                disabledUncheckedTrackColor = tokens.input.copy(alpha = 0.5f),
                disabledUncheckedBorderColor = Color.Transparent,
                disabledCheckedBorderColor = Color.Transparent,
            ),
    )
}

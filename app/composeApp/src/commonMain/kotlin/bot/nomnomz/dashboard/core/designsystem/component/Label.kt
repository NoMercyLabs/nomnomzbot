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

import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.text.font.FontWeight
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens
import bot.nomnomz.dashboard.core.designsystem.theme.Typography

// shadcn Label's disabled state dims to the field's own disabled opacity.
private const val LabelDisabledAlpha: Float = 0.5f

/**
 * shadcn/ui Label ported to Compose (frontend-design-system.md §4, catalogue row).
 *
 * Foundation-based, token-driven caption text pairing with a field. Medium-weight [Typography.sm]
 * over [Tokens.foreground]; [enabled] = false dims it to match a disabled sibling control. This is
 * the primitive [AppTextField] and other field patterns should compose for their field label going
 * forward, in place of an ad hoc `Text`.
 */
@Composable
fun Label(
    text: String,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
) {
    val tokens: Tokens = LocalTokens.current
    val typography: Typography = LocalTypography.current

    Text(
        text = text,
        modifier = if (enabled) modifier else modifier.alpha(LabelDisabledAlpha),
        style = typography.sm.copy(fontWeight = FontWeight.Medium),
        color = tokens.foreground,
    )
}

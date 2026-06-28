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

import androidx.compose.foundation.layout.size
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.PlainTooltip
import androidx.compose.material3.Text
import androidx.compose.material3.TooltipBox
import androidx.compose.material3.TooltipDefaults
import androidx.compose.material3.rememberTooltipState
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography

// A labelled icon-only action button:
//   • `label` is the visible tooltip on hover (mouse) and the contentDescription for screen readers.
//   • The icon is rendered at 16dp (spacing.s4) inside Material3's 40dp touch target.
//   • `tint` defaults to `tokens.foreground`; callers pass `tokens.destructive` for delete actions.
//
// Use this in place of a bare IconButton any time the button has no adjacent text label — it satisfies
// WCAG 1.3.1 (name visible on pointer hover) and 4.1.2 (name in accessibility tree).
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun GlyphButton(
    imageVector: ImageVector,
    label: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    tint: Color? = null,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val iconTint: Color = when {
        !enabled -> tokens.muted
        tint != null -> tint
        else -> tokens.foreground
    }

    TooltipBox(
        positionProvider = TooltipDefaults.rememberPlainTooltipPositionProvider(),
        tooltip = {
            PlainTooltip {
                Text(text = label, style = typography.xs, color = tokens.popoverForeground)
            }
        },
        state = rememberTooltipState(),
        modifier = modifier,
    ) {
        IconButton(
            onClick = onClick,
            enabled = enabled,
            modifier = Modifier.clearAndSetSemantics { contentDescription = label },
        ) {
            Icon(
                imageVector = imageVector,
                contentDescription = null,
                tint = iconTint,
                modifier = Modifier.size(spacing.s4),
            )
        }
    }
}

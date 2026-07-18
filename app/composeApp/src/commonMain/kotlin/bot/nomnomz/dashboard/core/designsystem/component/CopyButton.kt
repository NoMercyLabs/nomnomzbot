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
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import bot.nomnomz.dashboard.core.designsystem.icon.CheckCircleGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.CopyGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.io.copyToClipboard
import kotlinx.coroutines.delay

// The `CopyValue` design-system component: a read-only value the operator must paste into an external site
// (an OAuth redirect URI, a console URL) shown in a monospace-style chip with a copy-to-clipboard control.
// The write goes through [copyToClipboard] (expect/actual), which uses the legacy execCommand path on web so
// it works over plain http (a non-secure context, where Compose's LocalClipboardManager silently no-ops); the
// "copied" affordance flashes ONLY on a confirmed copy, never optimistically. The value is wrapped in a
// SelectionContainer so it can always be selected + copied by hand as a last resort. Both labels arrive
// already-localized; this component never holds a string.

private const val COPIED_FEEDBACK_MS: Long = 1500L

/**
 * Show [value] as a read-only, selectable-looking chip with a trailing copy control. Clicking copies [value]
 * to the system clipboard and flashes [copiedLabel] in place of [copyLabel] for a short moment. The labels
 * arrive localized from the caller (the design system holds no copy).
 */
@Composable
fun CopyValue(
    value: String,
    copyLabel: String,
    copiedLabel: String,
    modifier: Modifier = Modifier,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var copied: Boolean by remember(value) { mutableStateOf(false) }

    if (copied) {
        LaunchedEffect(value) {
            delay(COPIED_FEEDBACK_MS)
            copied = false
        }
    }

    Row(
        modifier = modifier
            .fillMaxWidth()
            .background(tokens.muted, RoundedCornerShape(tokens.radius.md))
            .border(width = spacing.s0_5 / 2, color = tokens.border, shape = RoundedCornerShape(tokens.radius.md))
            .padding(start = spacing.s3, end = spacing.s1, top = spacing.s1, bottom = spacing.s1),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        SelectionContainer(modifier = Modifier.weight(1f)) {
            Text(
                text = value,
                style = typography.xs,
                color = tokens.foreground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        TextButton(onClick = {
            if (copyToClipboard(value)) {
                copied = true
            }
        }) {
            Icon(
                imageVector = if (copied) CheckCircleGlyph else CopyGlyph,
                contentDescription = if (copied) copiedLabel else copyLabel,
                tint = if (copied) tokens.primary else tokens.mutedForeground,
                modifier = Modifier.size(spacing.s4),
            )
        }
    }
}

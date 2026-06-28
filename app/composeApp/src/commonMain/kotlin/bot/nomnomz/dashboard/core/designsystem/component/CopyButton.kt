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
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.style.TextOverflow
import bot.nomnomz.dashboard.core.designsystem.icon.CheckCircleGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.CopyGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import kotlinx.coroutines.delay

// The `CopyValue` design-system component: a read-only value the operator must paste into an external site
// (an OAuth redirect URI, a console URL) shown in a monospace-style chip with a copy-to-clipboard control.
// Cross-target (jvm + wasmJs) — the write goes through Compose's own `LocalClipboardManager`, so no
// expect/actual. The action label flips to its "copied" affordance for a moment after a copy, then settles
// back. Both the idle and copied labels are passed in already-localized; this component never holds a string.

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
    @Suppress("DEPRECATION") val clipboard = LocalClipboardManager.current

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
        Text(
            text = value,
            style = typography.xs,
            color = tokens.foreground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f),
        )
        TextButton(onClick = {
            clipboard.setText(AnnotatedString(value))
            copied = true
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

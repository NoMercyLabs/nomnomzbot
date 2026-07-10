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

import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.text.AnnotatedString
import bot.nomnomz.dashboard.core.designsystem.icon.CheckCircleGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.CopyGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import kotlinx.coroutines.delay

// The `CopyLinkButton` design-system component: a ghost action that copies a URL to the system clipboard so
// the operator can paste it into another device's browser instead of only opening it in place. It pairs
// beside an "open in browser" button on the device-code panels (streamer login, bot authorize, scope
// re-grant), where the code must be approved on a specific account/device. After a copy the glyph + label
// flip to the "copied" affordance for a moment, then settle back. Cross-target (jvm + wasmJs) — the write
// goes through Compose's own `LocalClipboardManager`, so no expect/actual. Both labels arrive already
// localized from the caller (the design system holds no copy), mirroring [CopyValue].

private const val COPY_LINK_FEEDBACK_MS: Long = 1500L

/**
 * A ghost [TextButton] that copies [url] to the system clipboard, flashing [copiedLabel] + a check glyph in
 * place of [copyLabel] + the copy glyph for a short moment after a copy. The labels arrive localized from the
 * caller — the design system never holds a string.
 */
@Composable
fun CopyLinkButton(
    url: String,
    copyLabel: String,
    copiedLabel: String,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    @Suppress("DEPRECATION") val clipboard = LocalClipboardManager.current

    var copied: Boolean by remember(url) { mutableStateOf(false) }

    if (copied) {
        LaunchedEffect(url) {
            delay(COPY_LINK_FEEDBACK_MS)
            copied = false
        }
    }

    TextButton(
        onClick = {
            clipboard.setText(AnnotatedString(url))
            copied = true
        },
        modifier = modifier,
        enabled = enabled,
    ) {
        Icon(
            imageVector = if (copied) CheckCircleGlyph else CopyGlyph,
            contentDescription = null,
            tint = if (copied) tokens.primary else tokens.mutedForeground,
            modifier = Modifier.size(spacing.s4),
        )
        Spacer(Modifier.width(spacing.s2))
        Text(text = if (copied) copiedLabel else copyLabel, maxLines = 1)
    }
}

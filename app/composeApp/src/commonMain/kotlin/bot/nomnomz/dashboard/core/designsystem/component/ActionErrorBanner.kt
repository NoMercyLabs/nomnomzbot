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

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import kotlinx.coroutines.delay

// Shared action-error banner: a full-width pill with destructive background used after a failed write
// (ban, toggle, save, etc.). The [message] arrives already-formatted from the caller's resource string.
//
// Self-hides after [AUTO_HIDE_MS]: every one of the ~44 call sites binds this to its own controller's
// `actionError`/write-error state field, and none of them wire a dismiss timer today, so the banner would
// otherwise sit forever until the next write. Timing it INSIDE the component (keyed on [message], so a
// fresh failure restarts the clock and re-shows) fixes every call site at once without threading a
// dismiss callback through 44 screens' state classes. The underlying state field itself is left set — a
// caller that re-renders the same message later (e.g. tab revisit) still shows it once more.
@Composable
fun ActionErrorBanner(message: String, modifier: Modifier = Modifier) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var visible: Boolean by remember(message) { mutableStateOf(true) }
    LaunchedEffect(message) {
        delay(AUTO_HIDE_MS)
        visible = false
    }

    AnimatedVisibility(visible = visible, modifier = modifier) {
        Text(
            text = message,
            style = typography.sm,
            color = tokens.destructiveForeground,
            modifier = Modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(tokens.radius.md))
                .background(tokens.destructive)
                .padding(horizontal = spacing.s3, vertical = spacing.s2),
        )
    }
}

private const val AUTO_HIDE_MS: Long = 10_000L

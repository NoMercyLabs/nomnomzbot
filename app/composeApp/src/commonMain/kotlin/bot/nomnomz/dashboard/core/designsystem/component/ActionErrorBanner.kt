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
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography

// Shared action-error banner: a full-width pill with destructive background used after a failed write
// (ban, toggle, save, etc.). The [message] arrives already-formatted from the caller's resource string.
@Composable
fun ActionErrorBanner(message: String, modifier: Modifier = Modifier) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Text(
        text = message,
        style = typography.sm,
        color = tokens.destructiveForeground,
        modifier = modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.md))
            .background(tokens.destructive)
            .padding(horizontal = spacing.s3, vertical = spacing.s2),
    )
}

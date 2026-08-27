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
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.unit.Dp
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import coil3.compose.AsyncImage

/**
 * Shows a Twitch (or other platform) profile avatar. When [imageUrl] is provided, renders it with a colored
 * background (acts as both placeholder and error fallback — the image covers it when loaded); otherwise shows
 * [name]'s first letter. The single shared avatar rendering used anywhere a person/channel needs a face —
 * the shell profile menu / channel switcher, the economy account list, and any future user-naming surface.
 */
@Composable
fun Avatar(name: String, size: Dp, imageUrl: String? = null) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    val initial: String = name.trim().firstOrNull()?.uppercase() ?: "?"

    Box(
        modifier = Modifier.size(size).clip(CircleShape).background(tokens.sidebarPrimary),
        contentAlignment = Alignment.Center,
    ) {
        if (imageUrl != null) {
            AsyncImage(
                model = imageUrl,
                contentDescription = name,
                modifier = Modifier.fillMaxSize(),
                contentScale = ContentScale.Crop,
            )
        } else {
            Text(text = initial, style = typography.sm, color = tokens.sidebarPrimaryForeground)
        }
    }
}

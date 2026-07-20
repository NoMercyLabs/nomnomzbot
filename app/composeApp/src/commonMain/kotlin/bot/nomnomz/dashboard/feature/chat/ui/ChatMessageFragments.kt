// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.chat.ui

import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRowScope
import androidx.compose.foundation.layout.size
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.media.AnimatedNetworkImage
import bot.nomnomz.dashboard.core.media.EmojiText
import bot.nomnomz.dashboard.core.network.ChatFragment

// The decorated body of a chat line — Twitch emote/cheermote fragments as inline images, mentions and links in
// their colours, and plain runs (which may carry Unicode emoji) through [EmojiText] so they render as images
// rather than □ tofu on the web build. Extracted from the primary chat feed's row so the multi-channel feed
// renders the exact same fragments; both callers host it inside their own [FlowRow], hence the receiver scope.
// Falls back to the flat [fallbackText] when [fragments] is empty (REST scrollback carries no fragments).
@OptIn(ExperimentalLayoutApi::class)
@Composable
internal fun FlowRowScope.ChatMessageFragments(
    fragments: List<ChatFragment>,
    fallbackText: String,
    emoteSize: Dp = 24.dp,
) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    if (fragments.isEmpty()) {
        EmojiText(text = fallbackText, style = typography.sm, color = tokens.cardForeground)
        return
    }

    fragments.forEach { fragment ->
        when (fragment.type) {
            "emote" -> {
                val url: String? = fragment.emote?.urls?.let { it["2"] ?: it["1"] ?: it.values.firstOrNull() }
                if (url != null) {
                    AnimatedNetworkImage(
                        url = url,
                        contentDescription = fragment.text,
                        modifier = Modifier.size(emoteSize).align(Alignment.CenterVertically),
                    )
                } else {
                    Text(text = fragment.text, style = typography.sm, color = tokens.cardForeground)
                }
            }
            "cheermote" -> {
                val url: String? = fragment.cheermote?.urls?.let { it["2"] ?: it["1"] ?: it.values.firstOrNull() }
                if (url != null) {
                    AnimatedNetworkImage(
                        url = url,
                        contentDescription = fragment.text,
                        modifier = Modifier.size(emoteSize).align(Alignment.CenterVertically),
                    )
                } else {
                    val tierColor: Color = fragment.cheermote?.colorHex?.toComposeColor() ?: tokens.cardForeground
                    Text(text = fragment.text, style = typography.sm, color = tierColor)
                }
            }
            "mention" -> {
                val mentionColor: Color = fragment.mention?.color?.toComposeColor() ?: tokens.primary
                Text(
                    text = "@${fragment.mention?.displayName?.takeIf { it.isNotBlank() } ?: fragment.text.removePrefix("@")}",
                    style = typography.sm,
                    color = mentionColor,
                )
            }
            "link" -> {
                Text(
                    text = fragment.text,
                    style = typography.sm.copy(textDecoration = TextDecoration.Underline),
                    color = tokens.primary,
                )
            }
            else -> {
                // Plain text run — may carry Unicode emoji, so render through [EmojiText] (inline Twemoji
                // images) rather than raw `Text`, which draws □ tofu on the web build.
                EmojiText(text = fragment.text, style = typography.sm, color = tokens.cardForeground)
            }
        }
    }
}

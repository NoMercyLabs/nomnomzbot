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

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRowScope
import androidx.compose.foundation.layout.size
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalUriHandler
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
    val uriHandler = LocalUriHandler.current

    if (fragments.isEmpty()) {
        PlainRun(text = fallbackText)
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
                // Backend-tagged link fragment — [linkUrl] carries the resolved target (bare `www.` links have no
                // scheme, so default to https for `openUri`); fall back to the visible text when it's absent.
                val target: String = fragment.linkUrl?.takeIf { it.isNotBlank() } ?: fragment.text
                Text(
                    text = fragment.text,
                    style = typography.sm.copy(textDecoration = TextDecoration.Underline),
                    color = tokens.primary,
                    modifier = Modifier.clickable { uriHandler.openUri(if ("://" in target) target else "https://$target") },
                )
            }
            else -> {
                // Plain text run — may carry Unicode emoji AND raw URLs. Twitch only tags emote/cheermote/mention
                // fragments, so any link lives inside a plain run and must be detected here.
                PlainRun(text = fragment.text)
            }
        }
    }
}

// A plain (untagged) chat run: splits out any URLs so they render as coloured, underlined links, and passes the
// non-link stretches through [EmojiText] (inline Twemoji images) rather than raw `Text`, which draws □ tofu on
// the web build. Each segment is its own [FlowRow] child, consistent with how tagged fragments already flow.
@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun FlowRowScope.PlainRun(text: String) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val uriHandler = LocalUriHandler.current

    val matches: Sequence<MatchResult> = UrlRegex.findAll(text)
    if (matches.none()) {
        EmojiText(text = text, style = typography.sm, color = tokens.cardForeground)
        return
    }

    var cursor = 0
    matches.forEach { match ->
        // Keep the URL itself, but leave any trailing sentence punctuation (".", ",", ")", …) as plain text so a
        // link at the end of a sentence doesn't swallow the period or a closing bracket that follows it.
        val url: String = match.value.trimEnd(*TrailingUrlPunctuation)
        val urlStart: Int = match.range.first
        val urlEnd: Int = urlStart + url.length

        if (urlStart > cursor) {
            EmojiText(text = text.substring(cursor, urlStart), style = typography.sm, color = tokens.cardForeground)
        }
        Text(
            text = url,
            style = typography.sm.copy(textDecoration = TextDecoration.Underline),
            color = tokens.primary,
            // Bare `www.` links have no scheme; `openUri` needs one, so default to https.
            modifier = Modifier.clickable { uriHandler.openUri(if ("://" in url) url else "https://$url") },
        )
        cursor = urlEnd
    }
    if (cursor < text.length) {
        EmojiText(text = text.substring(cursor), style = typography.sm, color = tokens.cardForeground)
    }
}

// URLs inside plain chat text. Two shapes:
//   1. an explicit `http(s)://…` or `www.…` run, taken greedily to the next whitespace; or
//   2. a bare domain with no scheme — any dotted host (`google.com`, `mysite.design`, `sub.example.co.uk/path`),
//      no TLD allow-list so new gTLDs (`.store`, `.dev`, …) just work. Two guards keep noise down: the final
//      label (the TLD) must be letters-only and 2+ chars, so decimals/versions like `3.5` or `v1.2` don't match;
//      and the leading lookbehind stops it firing inside an email address (`foo@bar.com`) or mid-token.
private val UrlRegex: Regex =
    Regex(
        """(?:https?://|www\.)\S+""" +
            """|(?<![\w@./-])(?:[a-z0-9-]+\.)+[a-z]{2,}(?::\d+)?(?:/\S*)?""",
        RegexOption.IGNORE_CASE,
    )

private val TrailingUrlPunctuation: CharArray = charArrayOf('.', ',', '!', '?', ';', ':', ')', ']', '}', '>', '"', '\'')

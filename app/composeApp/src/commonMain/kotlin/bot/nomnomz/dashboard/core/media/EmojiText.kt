// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.media

import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.text.InlineTextContent
import androidx.compose.foundation.text.appendInlineContent
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.Placeholder
import androidx.compose.ui.text.PlaceholderVerticalAlign
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.TextUnit
import androidx.compose.ui.unit.em
import coil3.compose.AsyncImage

// Text that renders Unicode emoji as inline Twemoji images (the FrankerFaceZ/Twitch approach) so they show as the
// real coloured glyphs on every target — crucially the web/Wasm build, where Compose has no colour-emoji font and
// a raw emoji would otherwise draw as a tofu box (□). Non-emoji text stays ordinary `Text`.
//
// Emoji are laid inline via Compose `InlineTextContent`, so they flow, wrap, and baseline-align with the
// surrounding words in a single text node (rather than being separate composables that would break line wrapping).
// The placeholder is sized in `em` so each emoji tracks the applied text style's size and sits centred on the line.
@Composable
fun EmojiText(
    text: String,
    style: TextStyle,
    color: Color,
    modifier: Modifier = Modifier,
    maxLines: Int = Int.MAX_VALUE,
    overflow: TextOverflow = TextOverflow.Clip,
) {
    val spans: List<EmojiSpan> = remember(text) { tokenizeEmoji(text) }
    val emojiSpans: List<EmojiSpan.Emoji> = remember(spans) { spans.filterIsInstance<EmojiSpan.Emoji>() }

    // Fast path: no emoji → a plain single-node `Text`, identical to the old rendering (no inline machinery).
    if (emojiSpans.isEmpty()) {
        Text(text = text, style = style, color = color, modifier = modifier, maxLines = maxLines, overflow = overflow)
        return
    }

    val annotated: AnnotatedString =
        remember(spans) {
            buildAnnotatedString {
                spans.forEach { span ->
                    when (span) {
                        is EmojiSpan.Text -> append(span.text)
                        // The image URL is the inline-content id; the raw emoji is the alternate (copy/a11y) text.
                        is EmojiSpan.Emoji -> appendInlineContent(span.imageUrl, span.raw)
                    }
                }
            }
        }

    val inlineContent: Map<String, InlineTextContent> =
        remember(emojiSpans) {
            emojiSpans.associate { emoji ->
                emoji.imageUrl to
                    InlineTextContent(
                        placeholder =
                            Placeholder(
                                width = EmojiInlineSize,
                                height = EmojiInlineSize,
                                placeholderVerticalAlign = PlaceholderVerticalAlign.Center,
                            ),
                    ) {
                        AsyncImage(
                            model = emoji.imageUrl,
                            contentDescription = emoji.raw,
                            modifier = Modifier.fillMaxSize(),
                        )
                    }
            }
        }

    Text(
        text = annotated,
        style = style,
        color = color,
        modifier = modifier,
        maxLines = maxLines,
        overflow = overflow,
        inlineContent = inlineContent,
    )
}

// Inline emoji size, relative to the applied text style so it matches the line (≈ the surrounding glyph height,
// like Twitch/FFZ chat). `em` keeps it scale-relative instead of a fixed dp that would drift from the text size.
private val EmojiInlineSize: TextUnit = 1.35.em

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

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

// Proves the emoji tokenizer that fixes the web-build "tofu box" render: a chat string must split into text spans
// and emoji spans, and each emoji cluster must map to the exact Twemoji filename (lowercase-hex code points joined
// by '-', with the twemoji.js FE0F rule) so the CDN image resolves. These are the cases the render depends on.
//
// Emoji inputs are built from explicit code points via [cp] rather than pasted glyphs, so the invisible members of
// a cluster (U+FE0F variation selector, U+200D ZWJ) are unambiguous and can't be lost by an editor.
class EmojiTokenizerTest {

    // Builds a UTF-16 string from Unicode code points (surrogate-encoding those above the BMP).
    private fun cp(vararg codePoints: Int): String =
        buildString {
            for (c in codePoints) {
                if (c <= 0xFFFF) {
                    append(c.toChar())
                } else {
                    val v: Int = c - 0x10000
                    append((0xD800 + (v shr 10)).toChar())
                    append((0xDC00 + (v and 0x3FF)).toChar())
                }
            }
        }

    private fun emoji(spans: List<EmojiSpan>, index: Int): EmojiSpan.Emoji =
        spans[index] as EmojiSpan.Emoji

    @Test
    fun single_codepoint_emoji_maps_to_its_twemoji_filename() {
        // 🤣 = U+1F923 → `1f923` (the owner's confirmed example).
        val rofl: String = cp(0x1F923)
        val spans: List<EmojiSpan> = tokenizeEmoji(rofl)

        assertEquals(1, spans.size)
        assertEquals("1f923", emoji(spans, 0).codepoints)
        assertEquals(rofl, emoji(spans, 0).raw)
        // Assert via twemojiUrl so the test verifies the codepoint→filename mapping, not the CDN host (which
        // can change): imageUrl is always "{TWEMOJI_CDN_BASE}/1f923.png".
        assertEquals(twemojiUrl("1f923"), emoji(spans, 0).imageUrl)
    }

    @Test
    fun plain_text_yields_a_single_text_span_and_no_images() {
        val spans: List<EmojiSpan> = tokenizeEmoji("hello world")

        assertEquals(listOf<EmojiSpan>(EmojiSpan.Text("hello world")), spans)
        assertTrue(spans.none { it is EmojiSpan.Emoji })
    }

    @Test
    fun emoji_adjacent_to_text_splits_into_text_then_emoji() {
        // "RIP🫠" → text "RIP" + emoji 🫠 (U+1FAE0). This emoji is Unicode 14.0; the tokenizer still splits it
        // correctly and produces `1fae0` even though the FFZ mirror doesn't host that image yet.
        val melting: String = cp(0x1FAE0)
        val spans: List<EmojiSpan> = tokenizeEmoji("RIP$melting")

        assertEquals(2, spans.size)
        assertEquals(EmojiSpan.Text("RIP"), spans[0])
        assertEquals("1fae0", emoji(spans, 1).codepoints)
        assertEquals(melting, emoji(spans, 1).raw)
    }

    @Test
    fun skin_tone_modifier_is_kept_as_one_cluster() {
        // 👍🏽 = U+1F44D U+1F3FD (medium skin tone) → single emoji `1f44d-1f3fd`.
        val spans: List<EmojiSpan> = tokenizeEmoji(cp(0x1F44D, 0x1F3FD))

        assertEquals(1, spans.size)
        assertEquals("1f44d-1f3fd", emoji(spans, 0).codepoints)
    }

    @Test
    fun zwj_sequence_is_one_cluster_joined_with_dashes() {
        // 👨‍👩‍👧 family = U+1F468 ZWJ U+1F469 ZWJ U+1F467 → `1f468-200d-1f469-200d-1f467`.
        val spans: List<EmojiSpan> =
            tokenizeEmoji(cp(0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467))

        assertEquals(1, spans.size)
        assertEquals("1f468-200d-1f469-200d-1f467", emoji(spans, 0).codepoints)
    }

    @Test
    fun variation_selector_is_stripped_when_there_is_no_zwj() {
        // ❤️ = U+2764 U+FE0F, no ZWJ → twemoji.js strips FE0F → `2764` (verified: `2764.png` exists, `2764-fe0f`
        // 404s on the CDN).
        val spans: List<EmojiSpan> = tokenizeEmoji(cp(0x2764, 0xFE0F))

        assertEquals(1, spans.size)
        assertEquals("2764", emoji(spans, 0).codepoints)
    }

    @Test
    fun variation_selector_is_kept_inside_a_zwj_sequence() {
        // 🏳️‍🌈 rainbow flag = U+1F3F3 U+FE0F ZWJ U+1F308 → twemoji KEEPS FE0F because a ZWJ is present →
        // `1f3f3-fe0f-200d-1f308` (verified 200 on the CDN; the FE0F-stripped variant 404s).
        val spans: List<EmojiSpan> = tokenizeEmoji(cp(0x1F3F3, 0xFE0F, 0x200D, 0x1F308))

        assertEquals(1, spans.size)
        assertEquals("1f3f3-fe0f-200d-1f308", emoji(spans, 0).codepoints)
    }

    @Test
    fun interleaved_text_and_emoji_preserve_order_and_spans() {
        // "hi 🤣 bye 🫠" → text, emoji, text, emoji — proves ordering and that text on both sides survives.
        val spans: List<EmojiSpan> = tokenizeEmoji("hi ${cp(0x1F923)} bye ${cp(0x1FAE0)}")

        assertEquals(4, spans.size)
        assertEquals(EmojiSpan.Text("hi "), spans[0])
        assertEquals("1f923", emoji(spans, 1).codepoints)
        assertEquals(EmojiSpan.Text(" bye "), spans[2])
        assertEquals("1fae0", emoji(spans, 3).codepoints)
    }

    @Test
    fun consecutive_emoji_are_separate_clusters() {
        // 🤣🫠 back-to-back → two distinct emoji spans (not one merged cluster).
        val spans: List<EmojiSpan> = tokenizeEmoji(cp(0x1F923, 0x1FAE0))

        assertEquals(2, spans.size)
        assertEquals("1f923", emoji(spans, 0).codepoints)
        assertEquals("1fae0", emoji(spans, 1).codepoints)
    }

    @Test
    fun regional_indicator_pair_is_one_flag_and_a_lone_indicator_stays_text() {
        // 🇺🇸 = U+1F1FA U+1F1F8 → one flag cluster `1f1fa-1f1f8`.
        val flag: List<EmojiSpan> = tokenizeEmoji(cp(0x1F1FA, 0x1F1F8))
        assertEquals(1, flag.size)
        assertEquals("1f1fa-1f1f8", emoji(flag, 0).codepoints)

        // A single regional indicator with no partner is not a flag — it must stay text, not become an image.
        val lone: List<EmojiSpan> = tokenizeEmoji(cp(0x1F1FA))
        assertTrue(lone.none { it is EmojiSpan.Emoji })
    }

    @Test
    fun bare_digit_is_text_but_a_keycap_sequence_is_one_emoji() {
        // A plain "7" is text, never an emoji image.
        assertTrue(tokenizeEmoji("7").none { it is EmojiSpan.Emoji })

        // 7️⃣ keycap = '7' U+FE0F U+20E3 → one emoji cluster; FE0F stripped (no ZWJ) → `37-20e3`.
        val keycap: List<EmojiSpan> = tokenizeEmoji(cp(0x37, 0xFE0F, 0x20E3))
        assertEquals(1, keycap.size)
        assertEquals("37-20e3", emoji(keycap, 0).codepoints)
    }

    @Test
    fun empty_string_yields_no_spans() {
        assertEquals(emptyList<EmojiSpan>(), tokenizeEmoji(""))
    }
}

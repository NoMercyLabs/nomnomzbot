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

// Proves the composer emoji picker's search + ranking: a query must surface every emoji whose shortcode OR keyword
// matches, with shortcode-prefix hits ranked ahead of keyword-only hits, so ":smi" puts :smile: before the emoji
// that merely lists "smile" as a keyword. The catalogue loader itself needs a Compose resource, so it is exercised
// live in-browser; this covers the pure ranking that the picker's usefulness depends on.
class EmojiCatalogTest {

    private val grinning: EmojiEntry =
        EmojiEntry(glyph = "😀", shortcodes = listOf("grinning"), keywords = "smile happy face", imageUrl = "u/1f600.png")
    private val smile: EmojiEntry =
        EmojiEntry(glyph = "😄", shortcodes = listOf("smile"), keywords = "happy joy laugh", imageUrl = "u/1f604.png")
    private val heart: EmojiEntry =
        EmojiEntry(glyph = "❤️", shortcodes = listOf("heart"), keywords = "love red", imageUrl = "u/2764.png")
    private val catalog: List<EmojiEntry> = listOf(grinning, smile, heart)

    @Test
    fun prefixShortcodeMatch_ranksAboveKeywordOnlyMatch() {
        // "smi" is a prefix of the :smile: shortcode and a substring of grinning's "smile" keyword — smile wins.
        val hits: List<EmojiEntry> = searchEmoji(catalog, "smi")
        assertEquals(listOf(smile, grinning), hits, "prefix shortcode hit ranks before keyword-only hit")
    }

    @Test
    fun keywordMatch_isFound_evenWithoutAShortcodeHit() {
        val hits: List<EmojiEntry> = searchEmoji(catalog, "love")
        assertEquals(listOf(heart), hits)
    }

    @Test
    fun noMatch_returnsEmpty() {
        assertTrue(searchEmoji(catalog, "xyzzy").isEmpty())
        assertTrue(searchEmoji(catalog, "  ").isEmpty(), "a blank query surfaces nothing")
    }

    @Test
    fun limit_capsResults() {
        val many: List<EmojiEntry> =
            (1..20).map { EmojiEntry(glyph = "😀", shortcodes = listOf("face$it"), keywords = "face", imageUrl = "u.png") }
        assertEquals(5, searchEmoji(many, "face", limit = 5).size)
    }
}

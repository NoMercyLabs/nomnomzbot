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

import bot.nomnomz.dashboard.core.network.ChatEmoteCatalogue
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

// Proves the composer's inline-emote transform: a draft must map to a display string where each emote code / emoji
// cluster is a width-reserving blank run, an image is placed at that run's start, and — critically — the two offset
// tables round-trip and stay monotonic. A broken OffsetMapping throws inside the live text field, so these are the
// invariants the field's stability depends on, asserted off-device where they can actually fail the build.
class ComposerInlineTest {

    private val emote: ChatEmoteCatalogue =
        ChatEmoteCatalogue(code = "verosWaving", urls = mapOf("2" to "https://cdn/veros.png"))
    private val emoteByCode: Map<String, ChatEmoteCatalogue> = mapOf(emote.code.lowercase() to emote)

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

    // Every offset mapping produced by the transform must satisfy OffsetMapping's contract, or the field crashes:
    // both directions non-decreasing over their whole domain, and the two endpoints pinned to the opposite length.
    private fun assertMappingIsValid(transform: ComposerInlineTransform, originalLength: Int) {
        val displayLength: Int = transform.display.length

        assertEquals(0, transform.mapOriginalToTransformed(0), "original 0 must map to transformed 0")
        assertEquals(
            displayLength,
            transform.mapOriginalToTransformed(originalLength),
            "end of original must map to end of display",
        )
        assertEquals(originalLength, transform.mapTransformedToOriginal(displayLength), "end of display → end of original")

        var previous = 0
        for (offset in 0..originalLength) {
            val mapped: Int = transform.mapOriginalToTransformed(offset)
            assertTrue(mapped in 0..displayLength, "original→transformed $offset out of range: $mapped")
            assertTrue(mapped >= previous, "original→transformed must be non-decreasing at $offset")
            previous = mapped
        }
        previous = 0
        for (offset in 0..displayLength) {
            val mapped: Int = transform.mapTransformedToOriginal(offset)
            assertTrue(mapped in 0..originalLength, "transformed→original $offset out of range: $mapped")
            assertTrue(mapped >= previous, "transformed→original must be non-decreasing at $offset")
            previous = mapped
        }
    }

    @Test
    fun plainText_isUnchanged_withIdentityMapping() {
        val draft = "hello world"
        val transform: ComposerInlineTransform = buildComposerInlineTransform(draft, emoteByCode, spacesPerImage = 3)

        assertEquals(draft, transform.display, "no emote/emoji → display equals draft")
        assertTrue(transform.images.isEmpty(), "no images for plain text")
        assertMappingIsValid(transform, draft.length)
        // Identity: every offset maps to itself.
        for (offset in 0..draft.length) assertEquals(offset, transform.mapOriginalToTransformed(offset))
    }

    @Test
    fun emoteCode_becomesBlankRun_withImageAtRunStart() {
        val draft = "hi verosWaving bye"
        val transform: ComposerInlineTransform = buildComposerInlineTransform(draft, emoteByCode, spacesPerImage = 3)

        // "hi " (3) + 3 blanks + " bye" (4) = 10 display chars; the code's 11 chars collapse to the 3-blank run.
        assertEquals(10, transform.display.length)
        assertEquals(1, transform.images.size)
        assertEquals(3, transform.images.first().transformedStart, "image sits where the reserved run begins")
        assertEquals("https://cdn/veros.png", transform.images.first().imageUrl)

        // The code start (original 3) maps to the run start; the space after the code (original 14) maps past it.
        assertEquals(3, transform.mapOriginalToTransformed(3))
        assertEquals(6, transform.mapOriginalToTransformed(14))
        // A caret anywhere inside the reserved run snaps back to the code start.
        assertEquals(3, transform.mapTransformedToOriginal(4))
        assertMappingIsValid(transform, draft.length)
    }

    @Test
    fun caseInsensitiveEmoteMatch_isRecognised() {
        val transform: ComposerInlineTransform =
            buildComposerInlineTransform("VEROSWAVING", emoteByCode, spacesPerImage = 4)
        assertEquals(1, transform.images.size, "emote match is case-insensitive")
        assertMappingIsValid(transform, "VEROSWAVING".length)
    }

    @Test
    fun unicodeEmoji_becomesTwemojiImage() {
        val smile: String = cp(0x1F604) // 😄
        val draft = "yo $smile"
        val transform: ComposerInlineTransform = buildComposerInlineTransform(draft, emoteByCode, spacesPerImage = 3)

        assertEquals(1, transform.images.size, "the emoji cluster becomes one inline image")
        assertTrue(transform.images.first().imageUrl.endsWith("1f604.png"), "emoji → its Twemoji code-point image")
        assertMappingIsValid(transform, draft.length)
    }

    @Test
    fun spacesPerImageIsClamped_soAnImageAlwaysReservesACaretStop() {
        val transform: ComposerInlineTransform =
            buildComposerInlineTransform("verosWaving", emoteByCode, spacesPerImage = 0)
        assertEquals(1, transform.display.length, "a zero request still reserves one blank")
        assertMappingIsValid(transform, "verosWaving".length)
    }
}

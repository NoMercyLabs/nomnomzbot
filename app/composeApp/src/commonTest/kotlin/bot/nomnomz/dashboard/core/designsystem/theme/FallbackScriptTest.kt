// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.theme

import kotlin.test.Test
import kotlin.test.assertEquals

// The dashboard loads a fallback face only when its script shows up in text the operator sees, so the first
// visit stays small. These tests pin which script each kind of text asks for and that the ask accumulates.
class FallbackScriptTest {

    @Test
    fun plain_latin_and_inter_covered_letters_ask_for_no_face() {
        assertEquals(emptySet(), FallbackScript.scriptsIn("plain latin text 123 !?"))
        assertEquals(emptySet(), FallbackScript.scriptsIn("Привет, mój Việt café"))
    }

    @Test
    fun a_korean_name_asks_for_hangul_only() {
        assertEquals(setOf(FallbackScript.Hangul), FallbackScript.scriptsIn("민준 the streamer"))
    }

    @Test
    fun japanese_and_chinese_text_asks_for_the_han_face() {
        assertEquals(setOf(FallbackScript.Han), FallbackScript.scriptsIn("こんにちは"))
        assertEquals(setOf(FallbackScript.Han), FallbackScript.scriptsIn("漢字 ＩＩ"))
    }

    @Test
    fun an_emoji_surrogate_pair_asks_for_the_emoji_face() {
        assertEquals(setOf(FallbackScript.Emoji), FallbackScript.scriptsIn("gg \uD83D\uDE00"))
    }

    @Test
    fun look_alike_spam_letters_ask_for_the_math_syllabics_and_cherokee_faces() {
        assertEquals(
            setOf(FallbackScript.Math, FallbackScript.CanadianAboriginal, FallbackScript.Cherokee),
            FallbackScript.scriptsIn("\uD835\uDDA6 \u1431 \u13EA"),
        )
    }

    @Test
    fun mixed_text_asks_for_every_script_it_holds() {
        assertEquals(
            setOf(FallbackScript.Arabic, FallbackScript.Thai, FallbackScript.ExtendedLatin),
            FallbackScript.scriptsIn("مرحبا สวัสดี नमस्ते"),
        )
    }

    @Test
    fun a_json_body_with_escaped_korean_is_decoded_before_detection() {
        assertEquals(
            setOf(FallbackScript.Hangul),
            FallbackScript.scriptsInJson("""{"name":"\ud55c\uae00","id":"12"}"""),
        )
    }

    @Test
    fun a_json_surrogate_pair_escape_asks_for_emoji_and_an_escaped_backslash_asks_for_nothing() {
        assertEquals(setOf(FallbackScript.Emoji), FallbackScript.scriptsInJson("""{"m":"\ud83d\ude00"}"""))
        assertEquals(emptySet(), FallbackScript.scriptsInJson("""{"path":"C:\\u0041\\ud55c"}"""))
    }

    @Test
    fun demand_accumulates_across_observations_and_never_shrinks() {
        val demand: FallbackFontDemand = FallbackFontDemand()
        assertEquals(emptySet(), demand.scripts.value)

        demand.observe("hello")
        assertEquals(emptySet(), demand.scripts.value)

        demand.observe("민준")
        demand.observeJson("""{"n":"\u3053"}""")
        demand.observe("hello again")

        assertEquals(setOf(FallbackScript.Hangul, FallbackScript.Han), demand.scripts.value)
    }
}

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

// Proves the composer autocomplete filter: typing ":wa" must surface every emote whose code CONTAINS "wa"
// (verosWaving, aaoaWat, basedcodeWave, …), not just codes that start with it. The old prefix-only filter
// collapsed a full 7TV/Twitch match set down to the two that happened to begin with the query.
class EmoteSuggestionsTest {

    private fun emote(code: String): ChatEmoteCatalogue = ChatEmoteCatalogue(code = code)

    @Test
    fun matches_emotes_that_contain_the_query_not_only_a_prefix() {
        val catalogue: List<ChatEmoteCatalogue> =
            listOf(
                emote("WatChuSay"), // prefix "Wa"
                emote("WAYTOODANK"), // prefix "WA"
                emote("verosWaving"), // "wa" mid-code
                emote("aaoaWat"), // "wa" mid-code
                emote("basedcodeWave"), // "wa" mid-code
                emote("Kappa"), // no "wa" at all
            )

        val codes: List<String> = emoteSuggestions(catalogue, "wa").map { it.code }

        // Every mid-code match surfaces — the old startsWith filter dropped exactly these.
        assertTrue("verosWaving" in codes)
        assertTrue("aaoaWat" in codes)
        assertTrue("basedcodeWave" in codes)
        // And the prefix matches are still present.
        assertTrue("WatChuSay" in codes)
        assertTrue("WAYTOODANK" in codes)
        // A non-matching code is excluded.
        assertTrue("Kappa" !in codes)
    }

    @Test
    fun ranks_prefix_matches_ahead_of_mid_code_matches() {
        val catalogue: List<ChatEmoteCatalogue> =
            listOf(
                emote("verosWaving"), // mid-code match, longer
                emote("WatChuSay"), // prefix match
                emote("aaoaWat"), // mid-code match, shorter
            )

        val codes: List<String> = emoteSuggestions(catalogue, "wa").map { it.code }

        // The prefix match leads regardless of input order; mid-code matches follow, shortest first.
        assertEquals(listOf("WatChuSay", "aaoaWat", "verosWaving"), codes)
    }

    @Test
    fun is_case_insensitive_and_caps_the_result_count() {
        val many: List<ChatEmoteCatalogue> = (1..20).map { emote("wave$it") }

        val result: List<ChatEmoteCatalogue> = emoteSuggestions(many, "WAVE", limit = 12)

        // Case-insensitive match, and the dropdown stays bounded to the cap.
        assertEquals(12, result.size)
    }
}

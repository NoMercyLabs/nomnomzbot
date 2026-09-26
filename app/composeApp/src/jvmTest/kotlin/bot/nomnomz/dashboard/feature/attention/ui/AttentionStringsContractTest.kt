// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.attention.ui

import java.io.File
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlin.test.fail

// The action-required inbox is rendered from backend resource KEYS. Three things must never drift apart: the
// keys the backend producers emit, the keys the dashboard maps to text, and the English and Dutch strings behind
// them. This reads all three from source, so a new producer key with no text, or a Dutch string that lost a
// placeholder, fails here instead of showing a blank or broken row.
class AttentionStringsContractTest {
    private val english: Map<String, String> =
        readStrings(File("src/commonMain/composeResources/values/strings_attention.xml"))
    private val dutch: Map<String, String> =
        readStrings(File("src/commonMain/composeResources/values-nl/strings_attention.xml"))
    private val mapping: String =
        File("src/commonMain/kotlin/bot/nomnomz/dashboard/feature/attention/ui/AttentionText.kt").readText()

    @Test
    fun english_and_dutch_carry_the_same_keys_with_the_same_placeholders() {
        assertTrue(english.isNotEmpty(), "no English attention strings parsed")
        assertEquals(english.keys, dutch.keys)
        english.forEach { (key, value) ->
            assertEquals(placeholders(value), placeholders(dutch.getValue(key)), "placeholders differ for $key")
            assertTrue(dutch.getValue(key).isNotBlank(), "empty Dutch text for $key")
        }
    }

    @Test
    fun every_key_a_backend_producer_emits_is_mapped_to_text() {
        val sources = File("../../server/src/NomNomzBot.Infrastructure/Notifications/Sources")
        if (!sources.isDirectory) fail("backend producers not found at ${sources.absolutePath}")
        val emitted: Set<String> =
            sources.walkTopDown()
                .filter { it.isFile && it.extension == "cs" }
                .flatMap { Regex("\"(attention_[a-z_]+)\"").findAll(it.readText()).map { m -> m.groupValues[1] } }
                .toSet()
        assertTrue(emitted.size >= 20, "expected every producer's keys, found ${emitted.size}")

        val unmapped: List<String> = emitted.filter { "\"$it\" ->" !in mapping }
        assertEquals(emptyList(), unmapped, "backend keys with no dashboard text")
    }

    @Test
    fun every_resource_the_mapping_uses_exists_in_both_languages() {
        val used: Set<String> =
            Regex("""Res\.string\.(attention_[a-z_]+)""").findAll(mapping).map { it.groupValues[1] }.toSet()

        assertEquals(emptySet(), used - english.keys, "used but missing in English")
        assertEquals(emptySet(), used - dutch.keys, "used but missing in Dutch")
    }

    private fun readStrings(file: File): Map<String, String> =
        Regex("""<string name="([^"]+)">([^<]*)</string>""")
            .findAll(file.readText())
            .associate { it.groupValues[1] to it.groupValues[2] }

    private fun placeholders(value: String): Set<String> =
        Regex("""%\d+\$[sd]""").findAll(value).map { it.value }.toSet()
}

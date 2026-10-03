// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.editor

import java.io.File
import java.util.Locale
import javax.xml.parsers.DocumentBuilderFactory
import kotlinx.coroutines.runBlocking
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.allStringResources
import org.jetbrains.compose.resources.ExperimentalResourceApi
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.getString
import org.jetbrains.compose.resources.getSystemResourceEnvironment
import org.w3c.dom.Element
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlin.test.fail

// The served code editor (editor.js) names every text it shows by a label id (DEFAULT_LABELS). The dashboard
// sends the words in its own language, so a label id without a string would show English in a Dutch dashboard.
// This guards both halves against the REAL editor.js and the REAL string resources.
@OptIn(ExperimentalResourceApi::class)
class EditorLabelsTest {

    @Test
    fun every_default_label_id_resolves_to_non_empty_text_in_english_and_dutch() = runBlocking {
        val ids: List<String> = defaultLabelIds()
        assertTrue(ids.size > 50, "editor.js should declare its labels; found ${ids.size}")

        val original: Locale = Locale.getDefault()
        try {
            for (language: String in listOf("en", "nl")) {
                Locale.setDefault(Locale.forLanguageTag(language))
                val problems: List<String> =
                    ids.mapNotNull { id ->
                        val resource: StringResource? = Res.allStringResources[EditorLabels.resourceName(id)]
                        when {
                            resource == null -> "$id (no ${EditorLabels.resourceName(id)} resource)"
                            getString(getSystemResourceEnvironment(), resource).isBlank() -> "$id (blank)"
                            else -> null
                        }
                    }
                assertEquals(emptyList(), problems, "label ids without $language text")
            }
        } finally {
            Locale.setDefault(original)
        }
    }

    @Test
    fun resolve_carries_every_default_label_id() = runBlocking {
        val resolved: Map<String, String> = EditorLabels.resolve()
        val missing: List<String> = defaultLabelIds().filter { resolved[it].isNullOrBlank() }
        assertEquals(emptyList(), missing, "label ids the app does not send")
    }

    @Test
    fun english_and_dutch_editor_strings_cover_the_same_keys_and_every_label_id() {
        val english: Set<String> = editorKeys("values")
        val dutch: Set<String> = editorKeys("values-nl")
        val expected: Set<String> = defaultLabelIds().map { EditorLabels.resourceName(it) }.toSet()

        assertEquals(emptySet(), english - dutch, "editor_ keys missing from values-nl")
        assertEquals(emptySet(), dutch - english, "editor_ keys missing from values")
        assertEquals(emptySet(), expected - english, "label ids without an editor_ string")
    }

    private fun defaultLabelIds(): List<String> {
        val script: String = fromRepoRoot("server/src/NomNomzBot.Api/Assets/editor/editor.js").readText()
        val start: Int = script.indexOf("const DEFAULT_LABELS = Object.freeze({")
        if (start < 0) fail("editor.js has no DEFAULT_LABELS block")
        val end: Int = script.indexOf("\n});", start)
        val block: String = script.substring(start, end)
        return Regex("(?m)^ {4}(\\w+):").findAll(block).map { it.groupValues[1] }.toList()
    }

    private fun editorKeys(variant: String): Set<String> =
        fromRepoRoot("app/composeApp/src/commonMain/composeResources/$variant")
            .listFiles { file -> file.extension == "xml" }
            .orEmpty()
            .flatMap { file ->
                val nodes = DocumentBuilderFactory.newInstance().newDocumentBuilder().parse(file)
                    .getElementsByTagName("string")
                (0 until nodes.length).map { (nodes.item(it) as Element).getAttribute("name") }
            }
            .filter { it.startsWith(EditorLabels.KEY_PREFIX) }
            .toSet()

    private fun fromRepoRoot(relative: String): File {
        var dir: File? = File(System.getProperty("user.dir"))
        while (dir != null) {
            val candidate = File(dir, relative)
            if (candidate.exists()) return candidate
            dir = dir.parentFile
        }
        fail("Could not locate $relative from ${System.getProperty("user.dir")}")
    }
}

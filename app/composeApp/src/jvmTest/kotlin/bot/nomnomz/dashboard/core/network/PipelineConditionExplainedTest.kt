// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.network

import bot.nomnomz.dashboard.feature.pipelines.ui.conditionHelpResource
import bot.nomnomz.dashboard.feature.pipelines.ui.conditionSummaryResource
import bot.nomnomz.dashboard.feature.pipelines.ui.operatorLabelResource
import java.io.File
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlin.test.fail

// S-PIPE-CONDITIONS-EXPLAINED (editor-ux-rules R8, R9, R10): the three conditions explain themselves, and the
// comparison operator is a picker over the exact words the server accepts, never free text.
class PipelineConditionExplainedTest {

    private val conditionTypes: List<String> = listOf("comparison", "random", "user_role")

    @Test
    fun the_operator_picker_offers_exactly_the_operators_the_server_accepts() {
        val serverWords: Set<String> = serverOperatorWords()

        assertTrue(serverWords.isNotEmpty(), "parsed no operators from ComparisonCondition.cs")
        assertEquals(serverWords, ComparisonOperators.values.toSet())
        assertEquals(ComparisonOperators.values.size, ComparisonOperators.values.toSet().size)
    }

    @Test
    fun the_comparison_operator_field_is_a_closed_choice_holding_the_server_values() {
        val palette: RuntimePalette =
            PipelineCatalogue.buildPalette(
                PipelineCatalogueRemote(conditions = listOf(PipelineConditionDescriptor(type = "comparison"))),
            )

        val operator: BlockField =
            assertNotNull(palette.condition("comparison")).fields.first { it.key == "operator" }

        assertEquals(ComparisonOperators.values, operator.options)
    }

    @Test
    fun every_operator_has_a_plain_label_and_an_unknown_stored_value_has_none() {
        for (value: String in ComparisonOperators.values) {
            assertNotNull(ComparisonOperators.labelKeyFor(value), "no label key for $value")
        }
        assertNull(ComparisonOperators.labelKeyFor("legacy_matches"))
    }

    @Test
    fun each_condition_carries_a_description_and_each_field_a_help_line() {
        for (type: String in conditionTypes) {
            val block: BlockType = assertNotNull(PipelineCatalogue.condition(type), type)
            assertNotNull(block.summaryKey, "$type has no description")
            for (field: BlockField in block.fields) {
                assertNotNull(field.helpKey, "$type.${field.key} has no help text")
            }
        }
    }

    @Test
    fun every_new_key_exists_in_english_and_dutch() {
        val keys: MutableList<String> = mutableListOf()
        for (type: String in conditionTypes) {
            val block: BlockType = assertNotNull(PipelineCatalogue.condition(type))
            keys += "pipelines_condition_summary_${block.summaryKey}"
            block.fields.forEach { keys += "pipelines_condition_help_${it.helpKey}" }
        }
        ComparisonOperators.values.forEach { keys += "pipelines_operator_${ComparisonOperators.labelKeyFor(it)}" }

        for (locale: String in listOf("values", "values-nl")) {
            val text: String = resourceFile(locale).readText()
            for (key: String in keys) {
                assertTrue(text.contains("name=\"$key\""), "$locale/strings.xml lacks $key")
            }
        }
    }

    @Test
    fun the_screen_maps_every_key_to_its_own_string_resource() {
        for (value: String in ComparisonOperators.values) {
            assertEquals("pipelines_operator_$value", assertNotNull(operatorLabelResource(value), value).key)
        }
        assertNull(operatorLabelResource("legacy_matches"))

        for (type: String in conditionTypes) {
            val block: BlockType = assertNotNull(PipelineCatalogue.condition(type))
            val summary: String = assertNotNull(block.summaryKey)
            assertEquals(
                "pipelines_condition_summary_$summary",
                assertNotNull(conditionSummaryResource(summary), type).key,
            )
            for (field: BlockField in block.fields) {
                val help: String = assertNotNull(field.helpKey)
                assertEquals("pipelines_condition_help_$help", assertNotNull(conditionHelpResource(help), help).key)
            }
        }
    }

    // ── helpers ──

    // The word-form operators in ComparisonCondition.TryParseOperator (symbol forms are aliases of these).
    private fun serverOperatorWords(): Set<String> {
        val source: String = repoFile("server/src/NomNomzBot.Infrastructure/Platform/Pipeline/CoreActions/ComparisonCondition.cs").readText()
        val parser: String = source.substringAfter("private static bool TryParseOperator")
        return Regex("case \"([a-z_]+)\":").findAll(parser).map { it.groupValues[1] }.toSet()
    }

    private fun resourceFile(folder: String): File =
        repoFile("app/composeApp/src/commonMain/composeResources/$folder/strings.xml")

    private fun repoFile(relative: String): File {
        var dir: File? = File(System.getProperty("user.dir"))
        while (dir != null) {
            val candidate = File(dir, relative)
            if (candidate.exists()) return candidate
            dir = dir.parentFile
        }
        fail("Could not locate $relative from ${System.getProperty("user.dir")}")
    }
}

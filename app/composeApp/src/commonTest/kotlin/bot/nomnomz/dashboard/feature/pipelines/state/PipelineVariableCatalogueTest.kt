// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.pipelines.state

import androidx.compose.ui.text.TextRange
import androidx.compose.ui.text.input.TextFieldValue
import bot.nomnomz.dashboard.core.network.BlockField
import bot.nomnomz.dashboard.core.network.BlockRole
import bot.nomnomz.dashboard.core.network.PaletteBlock
import bot.nomnomz.dashboard.core.network.PipelineNode
import bot.nomnomz.dashboard.core.network.PipelineStep
import bot.nomnomz.dashboard.core.network.RuntimePalette
import bot.nomnomz.dashboard.core.network.TemplateHelperDto
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class PipelineVariableCatalogueTest {

    private fun block(type: String, vararg fields: BlockField): PaletteBlock =
        PaletteBlock(
            type = type,
            role = BlockRole.Action,
            category = "",
            description = "",
            labelKey = null,
            fields = fields.toList(),
            hasHints = true,
        )

    private val palette: RuntimePalette =
        RuntimePalette(
            actions =
                listOf(
                    block(
                        "set_variable",
                        BlockField(key = "name", labelKey = "name", required = true, declaresVariable = true),
                        BlockField(key = "value", labelKey = "value", required = false),
                    ),
                    block(
                        "check_balance",
                        BlockField(
                            key = "variable",
                            labelKey = "variable",
                            required = false,
                            declaresVariable = true,
                            declaredVariableDefault = "balance",
                        ),
                    ),
                    block("send_message", BlockField(key = "message", labelKey = "message", required = true)),
                ),
            conditions = emptyList(),
        )

    private fun step(type: String, vararg params: Pair<String, String>): PipelineStep =
        PipelineStep(action = PipelineNode(type = type, params = mapOf(*params)))

    @Test
    fun only_variables_declared_by_earlier_steps_are_offered_with_their_sample() {
        val steps: List<PipelineStep> =
            listOf(
                step("set_variable", "name" to "greeting", "value" to "hello"),
                step("send_message", "message" to "x"),
                step("set_variable", "name" to "later", "value" to "no"),
            )

        val declared: List<DeclaredVariable> = declaredVariablesBefore(steps, index = 2, palette = palette)

        assertEquals(listOf(DeclaredVariable(name = "greeting", sample = "hello")), declared)
    }

    @Test
    fun an_empty_name_falls_back_to_the_declared_default_and_a_nameless_field_is_skipped() {
        val steps: List<PipelineStep> =
            listOf(step("check_balance"), step("set_variable", "name" to "  "), step("send_message"))

        val declared: List<DeclaredVariable> = declaredVariablesBefore(steps, index = 3, palette = palette)

        assertEquals(listOf("balance"), declared.map { it.name })
    }

    @Test
    fun a_variable_declared_twice_is_listed_once() {
        val steps: List<PipelineStep> =
            listOf(
                step("set_variable", "name" to "a", "value" to "1"),
                step("set_variable", "name" to "a", "value" to "2"),
            )

        assertEquals(listOf("a"), declaredVariablesBefore(steps, index = 2, palette = palette).map { it.name })
    }

    @Test
    fun picking_a_variable_mid_text_inserts_at_the_cursor_and_keeps_the_text_around_it() {
        val field: TextFieldValue = TextFieldValue(text = "Hello , welcome", selection = TextRange(6))

        val result: TextFieldValue = insertAtCursor(field, "{user.name}")

        assertEquals("Hello {user.name}, welcome", result.text)
        assertEquals(TextRange(17), result.selection)
    }

    @Test
    fun picking_a_variable_replaces_the_brace_fragment_typed_before_the_cursor() {
        val field: TextFieldValue = TextFieldValue(text = "Hi {us there", selection = TextRange(6))

        val result: TextFieldValue = insertAtCursor(field, "{user.name}")

        assertEquals("Hi {user.name} there", result.text)
        assertEquals(TextRange(14), result.selection)
    }

    @Test
    fun a_selection_is_replaced_by_the_variable() {
        val field: TextFieldValue = TextFieldValue(text = "say XXX now", selection = TextRange(4, 7))

        assertEquals("say {a} now", insertAtCursor(field, "{a}").text)
    }

    @Test
    fun the_brace_fragment_is_found_only_while_unclosed_and_without_spaces() {
        assertEquals("us", braceQuery(TextFieldValue("Hi {us", TextRange(6))))
        assertEquals("", braceQuery(TextFieldValue("Hi {", TextRange(4))))
        assertNull(braceQuery(TextFieldValue("Hi {user} ", TextRange(10))))
        assertNull(braceQuery(TextFieldValue("Hi {us er", TextRange(9))))
        assertNull(braceQuery(TextFieldValue("no brace", TextRange(8))))
    }

    @Test
    fun options_merge_declared_variables_first_then_helpers_and_filter_by_key() {
        val helpers: List<TemplateHelperDto> =
            listOf(
                TemplateHelperDto(key = "user.name", descriptionKey = "d1", sample = "Stoney"),
                TemplateHelperDto(key = "counter.deaths", descriptionKey = "d2", sample = "3"),
            )
        val declared: List<DeclaredVariable> = listOf(DeclaredVariable("greeting", "hello"))

        val all: List<VariableOption> = variableOptions(declared, helpers)
        assertEquals(listOf("greeting", "user.name", "counter.deaths"), all.map { it.key })
        assertEquals("hello", all.first().sample)
        assertNull(all.first().descriptionKey)

        assertEquals(listOf("counter.deaths"), filterVariableOptions(all, "DEATH").map { it.key })
        assertEquals(all, filterVariableOptions(all, ""))
    }
}

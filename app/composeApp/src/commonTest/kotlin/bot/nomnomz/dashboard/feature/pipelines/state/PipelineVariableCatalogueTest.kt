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

    private fun setVar(id: String, name: String, parent: String? = null, branch: String? = null, order: Int): PipelineStep =
        PipelineStep(
            action = PipelineNode(type = "set_variable", params = mapOf("name" to name)),
            id = id,
            parentStepId = parent,
            branch = branch,
            order = order,
        )

    private fun ifBlock(id: String, parent: String? = null, branch: String? = null, order: Int): PipelineStep =
        PipelineStep(action = PipelineNode(type = "block"), id = id, parentStepId = parent, branch = branch, blockKind = "if", order = order)

    // root: before, if1 { inner1, if2 { deep }, innerAfter }, after
    private val tree: List<PipelineStep> =
        listOf(
            setVar("a", "before", order = 0),
            ifBlock("if1", order = 1),
            setVar("c", "after", order = 2),
            setVar("i1", "inner1", parent = "if1", branch = "then", order = 0),
            ifBlock("if2", parent = "if1", branch = "then", order = 1),
            setVar("i3", "innerAfter", parent = "if1", branch = "then", order = 2),
            setVar("d", "deep", parent = "if2", branch = "then", order = 0),
        )

    @Test
    fun inside_an_if_lane_only_what_runs_before_it_is_offered() {
        val declared: List<DeclaredVariable> =
            declaredVariablesInScope(tree, VariableScope(parentStepId = "if1", branch = "then"), palette)

        // The end of the lane: its own steps are all before the spot; the nested lane's "deep" and the root's "after" are not.
        assertEquals(listOf("before", "inner1", "innerAfter"), declared.map { it.name })
    }

    @Test
    fun a_block_nested_two_deep_sees_every_enclosing_lane_up_to_the_blocks_but_nothing_after() {
        val declared: List<DeclaredVariable> =
            declaredVariablesInScope(tree, VariableScope(parentStepId = "if2", branch = "then"), palette)

        assertEquals(listOf("before", "inner1", "deep"), declared.map { it.name })
    }

    @Test
    fun editing_a_step_in_a_lane_scopes_to_the_position_in_front_of_it() {
        val declared: List<DeclaredVariable> =
            declaredVariablesInScope(tree, VariableScope(parentStepId = "if1", branch = "then", beforeStepId = "if2"), palette)

        assertEquals(listOf("before", "inner1"), declared.map { it.name })
        assertEquals(
            listOf("before"),
            declaredVariablesInScope(tree, VariableScope(parentStepId = "if1", branch = "then", beforeStepId = "i1"), palette).map { it.name },
        )
    }

    @Test
    fun the_root_scope_sees_the_whole_root_lane_and_none_of_the_nested_lanes() {
        val declared: List<DeclaredVariable> = declaredVariablesInScope(tree, VariableScope(), palette)

        assertEquals(listOf("before", "after"), declared.map { it.name })
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

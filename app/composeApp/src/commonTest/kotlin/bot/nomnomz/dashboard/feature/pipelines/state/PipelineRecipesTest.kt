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

import bot.nomnomz.dashboard.core.network.BlockField
import bot.nomnomz.dashboard.core.network.PipelineCatalogue
import bot.nomnomz.dashboard.core.network.PipelineGraph
import bot.nomnomz.dashboard.core.network.PipelineNode
import bot.nomnomz.dashboard.core.network.PipelineStep
import bot.nomnomz.dashboard.core.network.RuntimePalette
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest
import org.jetbrains.compose.resources.getString

/**
 * S-PIPE-RECIPES step A: each recipe is a ready-made graph built from blocks the palette really has, with every
 * required field filled and every text resolvable, and with the shape its goal needs.
 */
class PipelineRecipesTest {

    private val palette: RuntimePalette = PipelineCatalogue.fallbackPalette()

    private suspend fun graphOf(id: String): PipelineGraph =
        PipelineRecipes.graph(PipelineRecipes.all.single { it.id == id })

    private fun PipelineGraph.children(parent: PipelineStep, branch: String?): List<PipelineStep> =
        steps.filter { it.parentStepId == parent.id && it.branch == branch }.sortedBy { it.order }

    private fun PipelineGraph.topLevel(): List<PipelineStep> =
        steps.filter { it.parentStepId == null }.sortedBy { it.order }

    @Test
    fun there_are_four_recipes_with_unique_ids() {
        assertEquals(4, PipelineRecipes.all.size)
        assertEquals(4, PipelineRecipes.all.map { it.id }.toSet().size)
    }

    @Test
    fun every_block_exists_in_the_palette_and_every_required_field_is_filled() = runTest {
        for (recipe in PipelineRecipes.all) {
            val graph: PipelineGraph = PipelineRecipes.graph(recipe)
            assertTrue(graph.steps.isNotEmpty(), "${recipe.id} has steps")
            for (step in graph.steps) {
                if (step.action.type == "block") {
                    assertTrue(step.blockKind in setOf("if", "random_branch", "random_case"), "${recipe.id} block kind")
                } else {
                    checkNode(recipe.id, step.action, palette.action(step.action.type)?.fields, "action")
                }
                step.condition?.let { checkNode(recipe.id, it, palette.condition(it.type)?.fields, "condition") }
            }
        }
    }

    private fun checkNode(recipeId: String, node: PipelineNode, fields: List<BlockField>?, role: String) {
        assertNotNull(fields, "$recipeId: $role ${node.type} is not in the palette")
        for (field in fields.filter { it.required }) {
            assertTrue(!node.params[field.key].isNullOrBlank(), "$recipeId: ${node.type} needs '${field.key}'")
        }
    }

    @Test
    fun every_parent_link_points_at_a_step_and_ids_are_unique() = runTest {
        for (recipe in PipelineRecipes.all) {
            val graph: PipelineGraph = PipelineRecipes.graph(recipe)
            val ids: List<String> = graph.steps.mapNotNull { it.id }
            assertEquals(graph.steps.size, ids.toSet().size, "${recipe.id} ids unique")
            for (step in graph.steps) {
                step.parentStepId?.let { assertTrue(it in ids, "${recipe.id} parent $it exists") }
            }
        }
    }

    @Test
    fun every_text_key_resolves_to_real_text() = runTest {
        for (recipe in PipelineRecipes.all) {
            assertTrue(getString(recipe.title).isNotBlank())
            assertTrue(getString(recipe.description).isNotBlank())
            for (reply in recipe.replies) assertTrue(getString(reply).isNotBlank())
            val graph: PipelineGraph = PipelineRecipes.graph(recipe)
            val sent: List<String> =
                graph.steps.filter { it.action.type == "send_reply" }.map { it.action.params.getValue("message") }
            assertEquals(recipe.replies.size, sent.size, "${recipe.id} one reply step per text")
            for (text in sent) assertTrue(text.isNotBlank() && !text.startsWith("res:"))
        }
    }

    @Test
    fun every_nth_counts_then_resets_the_counter_inside_the_if() = runTest {
        val graph: PipelineGraph = graphOf("every_nth")
        val top: List<PipelineStep> = graph.topLevel()
        assertEquals(listOf("adjust_viewer_data", "block"), top.map { it.action.type })
        assertEquals("redeem_count", top[0].action.params["key"])
        assertEquals("1", top[0].action.params["delta"])
        val ifStep: PipelineStep = top[1]
        assertEquals("if", ifStep.blockKind)
        assertEquals("comparison", ifStep.condition?.type)
        assertEquals("{viewer.data.redeem_count}", ifStep.condition?.params?.get("left"))
        assertEquals("gte", ifStep.condition?.params?.get("operator"))
        assertEquals("5", ifStep.condition?.params?.get("right"))
        val then: List<PipelineStep> = graph.children(ifStep, "then")
        assertEquals(listOf("send_reply", "set_viewer_data"), then.map { it.action.type })
        assertEquals("redeem_count", then[1].action.params["key"])
        assertEquals("0", then[1].action.params["value"])
    }

    @Test
    fun chance_chain_replies_only_inside_a_random_if_defaulting_to_25() = runTest {
        val graph: PipelineGraph = graphOf("chance_chain")
        val top: List<PipelineStep> = graph.topLevel()
        assertEquals(1, top.size)
        assertEquals("if", top[0].blockKind)
        assertEquals("random", top[0].condition?.type)
        assertEquals("25", top[0].condition?.params?.get("percent"))
        assertEquals(listOf("send_reply"), graph.children(top[0], "then").map { it.action.type })
    }

    @Test
    fun random_reply_has_three_cases_each_with_one_reply() = runTest {
        val graph: PipelineGraph = graphOf("random_reply")
        val top: List<PipelineStep> = graph.topLevel()
        assertEquals(1, top.size)
        assertEquals("random_branch", top[0].blockKind)
        val cases: List<PipelineStep> = graph.children(top[0], null)
        assertEquals(3, cases.size)
        for (case in cases) {
            assertEquals("random_case", case.blockKind)
            assertEquals(listOf("send_reply"), graph.children(case, null).map { it.action.type })
        }
    }

    @Test
    fun count_remember_adds_one_then_replies_with_the_count_variable() = runTest {
        val graph: PipelineGraph = graphOf("count_remember")
        val top: List<PipelineStep> = graph.topLevel()
        assertEquals(listOf("adjust_viewer_data", "send_reply"), top.map { it.action.type })
        assertEquals("1", top[0].action.params["delta"])
        val key: String = top[0].action.params.getValue("key")
        assertTrue(top[1].action.params.getValue("message").contains("{viewer.data.$key}"))
    }

    @Test
    fun a_recipe_graph_survives_a_json_round_trip() = runTest {
        for (recipe in PipelineRecipes.all) {
            val graph: PipelineGraph = PipelineRecipes.graph(recipe)
            assertEquals(graph, PipelineGraph.fromJson(graph.toJson()), recipe.id)
        }
    }
}

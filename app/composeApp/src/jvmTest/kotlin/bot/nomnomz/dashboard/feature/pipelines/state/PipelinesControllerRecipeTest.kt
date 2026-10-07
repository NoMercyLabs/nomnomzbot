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

import bot.nomnomz.dashboard.core.network.PipelineGraph
import bot.nomnomz.dashboard.core.network.PipelineStep
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/** Creating a pipeline from a recipe saves the recipe's graph; with no recipe it saves the empty graph. */
class PipelinesControllerRecipeTest {

    private fun recipe(id: String): PipelineRecipe = PipelineRecipes.all.first { it.id == id }

    private suspend fun savedSteps(recipe: PipelineRecipe?): List<PipelineStep> {
        val api = RecordingPipelinesApiForRecipeTest()
        val controller: PipelinesController = recipeTestController(api)
        controller.load()
        controller.createPipeline("Mine", "d", recipe)
        assertEquals(1, api.created.size, "exactly one create call")
        assertEquals("Mine", api.created[0].name)
        return PipelineGraph.fromJson(api.created[0].graph).steps
    }

    @Test
    fun without_a_recipe_the_empty_graph_is_saved() = runTest {
        assertEquals(emptyList(), savedSteps(null))
    }

    @Test
    fun every_nth_saves_count_then_a_gate_with_reply_and_reset() = runTest {
        val steps: List<PipelineStep> = savedSteps(recipe("every_nth")).sortedWith(compareBy({ it.parentStepId ?: "" }, { it.order }))
        val top: List<PipelineStep> = steps.filter { it.parentStepId == null }
        assertEquals(listOf("adjust_viewer_data", "block"), top.map { it.action.type })
        assertEquals("if", top[1].blockKind)
        val inside: List<PipelineStep> = steps.filter { it.parentStepId == top[1].id }
        assertEquals(listOf("send_reply", "set_viewer_data"), inside.map { it.action.type })
        assertTrue(inside[0].action.params["message"].orEmpty().isNotBlank(), "the reply text is resolved, not a key")
    }

    @Test
    fun random_reply_saves_one_random_branch_with_three_cases_each_replying() = runTest {
        val steps: List<PipelineStep> = savedSteps(recipe("random_reply"))
        val branch: PipelineStep = steps.single { it.blockKind == "random_branch" }
        val cases: List<PipelineStep> = steps.filter { it.parentStepId == branch.id }
        assertEquals(3, cases.size)
        for (case: PipelineStep in cases) {
            val child: PipelineStep = steps.single { it.parentStepId == case.id }
            assertEquals("send_reply", child.action.type)
        }
    }

    @Test
    fun chance_and_count_recipes_save_their_step_order() = runTest {
        assertEquals(listOf("block", "send_reply"), savedSteps(recipe("chance_chain")).map { it.action.type })
        assertEquals(listOf("adjust_viewer_data", "send_reply"), savedSteps(recipe("count_remember")).map { it.action.type })
    }
}

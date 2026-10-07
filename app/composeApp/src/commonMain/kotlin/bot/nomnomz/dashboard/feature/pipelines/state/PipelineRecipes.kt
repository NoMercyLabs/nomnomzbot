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
import bot.nomnomz.dashboard.core.network.PipelineNode
import bot.nomnomz.dashboard.core.network.PipelineStep
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.pipelines_recipe_chance_chain_desc
import nomnomzbot.composeapp.generated.resources.pipelines_recipe_chance_chain_reply_1
import nomnomzbot.composeapp.generated.resources.pipelines_recipe_chance_chain_title
import nomnomzbot.composeapp.generated.resources.pipelines_recipe_count_remember_desc
import nomnomzbot.composeapp.generated.resources.pipelines_recipe_count_remember_reply_1
import nomnomzbot.composeapp.generated.resources.pipelines_recipe_count_remember_title
import nomnomzbot.composeapp.generated.resources.pipelines_recipe_every_nth_desc
import nomnomzbot.composeapp.generated.resources.pipelines_recipe_every_nth_reply_1
import nomnomzbot.composeapp.generated.resources.pipelines_recipe_every_nth_title
import nomnomzbot.composeapp.generated.resources.pipelines_recipe_random_reply_desc
import nomnomzbot.composeapp.generated.resources.pipelines_recipe_random_reply_reply_1
import nomnomzbot.composeapp.generated.resources.pipelines_recipe_random_reply_reply_2
import nomnomzbot.composeapp.generated.resources.pipelines_recipe_random_reply_reply_3
import nomnomzbot.composeapp.generated.resources.pipelines_recipe_random_reply_title
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.getString

/**
 * A ready-made pipeline for a common goal. [title] and [description] are shown in the picker; [replies] are the
 * reply texts, resolved in the current language when the recipe is applied ([PipelineRecipes.graph]) and handed
 * to [build] in order. The result is the same [PipelineGraph] type `createPipeline` saves.
 */
class PipelineRecipe(
    val id: String,
    val title: StringResource,
    val description: StringResource,
    val replies: List<StringResource>,
    private val build: (List<String>) -> PipelineGraph,
) {
    fun graph(resolvedReplies: List<String>): PipelineGraph = build(resolvedReplies)
}

/** The recipes the new-pipeline picker offers. Neutral defaults only: the streamer edits the numbers and words. */
object PipelineRecipes {

    private const val COUNTER_KEY: String = "redeem_count"
    private const val EVERY_NTH: Int = 5
    private const val CHANCE_PERCENT: Int = 25

    val all: List<PipelineRecipe> =
        listOf(
            PipelineRecipe(
                id = "every_nth",
                title = Res.string.pipelines_recipe_every_nth_title,
                description = Res.string.pipelines_recipe_every_nth_desc,
                replies = listOf(Res.string.pipelines_recipe_every_nth_reply_1),
                build = ::everyNth,
            ),
            PipelineRecipe(
                id = "chance_chain",
                title = Res.string.pipelines_recipe_chance_chain_title,
                description = Res.string.pipelines_recipe_chance_chain_desc,
                replies = listOf(Res.string.pipelines_recipe_chance_chain_reply_1),
                build = ::chanceChain,
            ),
            PipelineRecipe(
                id = "random_reply",
                title = Res.string.pipelines_recipe_random_reply_title,
                description = Res.string.pipelines_recipe_random_reply_desc,
                replies =
                    listOf(
                        Res.string.pipelines_recipe_random_reply_reply_1,
                        Res.string.pipelines_recipe_random_reply_reply_2,
                        Res.string.pipelines_recipe_random_reply_reply_3,
                    ),
                build = ::randomReply,
            ),
            PipelineRecipe(
                id = "count_remember",
                title = Res.string.pipelines_recipe_count_remember_title,
                description = Res.string.pipelines_recipe_count_remember_desc,
                replies = listOf(Res.string.pipelines_recipe_count_remember_reply_1),
                build = ::countRemember,
            ),
        )

    /** The recipe's graph with its reply texts resolved in the current language. */
    suspend fun graph(recipe: PipelineRecipe): PipelineGraph =
        recipe.graph(recipe.replies.map { getString(it) })

    // ── Graph builders ───────────────────────────────────────────────────────

    private fun everyNth(replies: List<String>): PipelineGraph =
        PipelineGraph(
            listOf(
                adjustViewerData(id = "count", order = 0),
                ifBlock(
                    id = "gate",
                    order = 1,
                    condition =
                        PipelineNode(
                            "comparison",
                            mapOf("left" to viewerVariable(), "operator" to "gte", "right" to EVERY_NTH.toString()),
                        ),
                ),
                sendReply(id = "reward", text = replies[0], parent = "gate", branch = "then", order = 0),
                PipelineStep(
                    action = PipelineNode("set_viewer_data", mapOf("key" to COUNTER_KEY, "value" to "0")),
                    id = "reset",
                    parentStepId = "gate",
                    branch = "then",
                    order = 1,
                ),
            )
        )

    private fun chanceChain(replies: List<String>): PipelineGraph =
        PipelineGraph(
            listOf(
                ifBlock(
                    id = "chance",
                    order = 0,
                    condition = PipelineNode("random", mapOf("percent" to CHANCE_PERCENT.toString())),
                ),
                sendReply(id = "bonus", text = replies[0], parent = "chance", branch = "then", order = 0),
            )
        )

    private fun randomReply(replies: List<String>): PipelineGraph {
        val steps: MutableList<PipelineStep> =
            mutableListOf(
                PipelineStep(
                    action = PipelineNode("block"),
                    id = "pick",
                    blockKind = "random_branch",
                    order = 0,
                )
            )
        replies.forEachIndexed { index: Int, text: String ->
            val caseId = "case_${index + 1}"
            steps +=
                PipelineStep(
                    action = PipelineNode("block"),
                    id = caseId,
                    parentStepId = "pick",
                    blockKind = "random_case",
                    blockConfig = JsonObject(mapOf("weight" to JsonPrimitive(1))),
                    order = index,
                )
            steps += sendReply(id = "reply_${index + 1}", text = text, parent = caseId, branch = null, order = 0)
        }
        return PipelineGraph(steps)
    }

    private fun countRemember(replies: List<String>): PipelineGraph =
        PipelineGraph(
            listOf(
                adjustViewerData(id = "count", order = 0),
                sendReply(id = "say", text = replies[0], parent = null, branch = null, order = 1),
            )
        )

    // ── Step helpers ─────────────────────────────────────────────────────────

    private fun viewerVariable(): String = "{viewer.data.$COUNTER_KEY}"

    private fun adjustViewerData(id: String, order: Int): PipelineStep =
        PipelineStep(
            action = PipelineNode("adjust_viewer_data", mapOf("key" to COUNTER_KEY, "delta" to "1")),
            id = id,
            order = order,
        )

    private fun ifBlock(id: String, order: Int, condition: PipelineNode): PipelineStep =
        PipelineStep(
            action = PipelineNode("block"),
            condition = condition,
            id = id,
            blockKind = "if",
            order = order,
        )

    private fun sendReply(id: String, text: String, parent: String?, branch: String?, order: Int): PipelineStep =
        PipelineStep(
            action = PipelineNode("send_reply", mapOf("message" to text)),
            id = id,
            parentStepId = parent,
            branch = branch,
            order = order,
        )
}

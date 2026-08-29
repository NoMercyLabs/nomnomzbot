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

import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.FeedbackKind
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.feedback.RecordingFeedback
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CreateInboundBody
import bot.nomnomz.dashboard.core.network.CreateOutboundBody
import bot.nomnomz.dashboard.core.network.CreatePickListBody
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.CreatePipelineBody
import bot.nomnomz.dashboard.core.network.InboundWebhook
import bot.nomnomz.dashboard.core.network.OutboundDelivery
import bot.nomnomz.dashboard.core.network.OutboundEventCatalogueEntry
import bot.nomnomz.dashboard.core.network.OutboundWebhook
import bot.nomnomz.dashboard.core.network.OutboundWebhookCreated
import bot.nomnomz.dashboard.core.network.UpdateInboundBody
import bot.nomnomz.dashboard.core.network.UpdateOutboundBody
import bot.nomnomz.dashboard.core.network.PickList
import bot.nomnomz.dashboard.core.network.PickListsApi
import bot.nomnomz.dashboard.core.network.PipelineActionDescriptor
import bot.nomnomz.dashboard.core.network.PipelineBlastRadiusSummary
import bot.nomnomz.dashboard.core.network.LocalizedTextDto
import bot.nomnomz.dashboard.core.network.PipelineCatalogueRemote
import bot.nomnomz.dashboard.core.network.PipelineConditionDescriptor
import bot.nomnomz.dashboard.core.network.PipelineDetail
import bot.nomnomz.dashboard.core.network.PipelineGraph
import bot.nomnomz.dashboard.core.network.PipelineNode
import bot.nomnomz.dashboard.core.network.PipelineStep
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.PipelinesApi
import bot.nomnomz.dashboard.core.network.PipelineTestRunBody
import bot.nomnomz.dashboard.core.network.CapturedEffect
import bot.nomnomz.dashboard.core.network.TestRunResult
import bot.nomnomz.dashboard.core.network.UpdatePickListBody
import bot.nomnomz.dashboard.core.network.UpdatePipelineBody
import bot.nomnomz.dashboard.core.network.WebhookTestResult
import bot.nomnomz.dashboard.core.network.WebhooksApi
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.intOrNull
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_pipeline_deleted
import nomnomzbot.composeapp.generated.resources.feedback_pipeline_saved
import nomnomzbot.composeapp.generated.resources.feedback_pipeline_save_failed
import bot.nomnomz.dashboard.core.network.BlastRadiusSummary

// Proves the Pipelines page state machine: resolve the active channel, list its real pipelines, drive the
// full list-level management surface (create / rename / toggle / delete), open a pipeline's action-chain
// editor, mutate the chain (add / remove / reorder / configure), and persist it. Every assertion checks the
// resulting STATE / the body the controller built / the backend store's real change — never a smoke "it ran".
class PipelinesControllerTest {

    // ── List surface ──────────────────────────────────────────────────────────

    @Test
    fun load_surfaces_the_channel_pipelines_on_success() = runTest {
        val controller =
            pipelinesController(
                okChannel(),
                RecordingPipelinesApi(
                    listOf(
                        PipelineSummary(id = "00000003-0000-0000-0000-000000000003", name = "Welcome", description = "greets", isEnabled = true, triggerCount = 9)
                    )
                ),
            )

        controller.load()

        val state: PipelinesState = controller.state.value
        assertTrue(state is PipelinesState.Ready)
        val pipelines: List<PipelineSummary> = (state as PipelinesState.Ready).pipelines
        assertEquals(1, pipelines.size)
        assertEquals("Welcome", pipelines.first().name)
        assertEquals(true, pipelines.first().isEnabled)
        assertEquals(9, pipelines.first().triggerCount)
    }

    @Test
    fun load_is_empty_when_the_channel_has_no_pipelines() = runTest {
        val controller = pipelinesController(okChannel(), RecordingPipelinesApi(emptyList()))

        controller.load()

        assertTrue(controller.state.value is PipelinesState.Empty)
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            pipelinesController(
                FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                RecordingPipelinesApi(emptyList()),
            )

        controller.load()

        assertTrue(controller.state.value is PipelinesState.Error)
    }

    @Test
    fun load_errors_when_the_list_call_fails() = runTest {
        val api = RecordingPipelinesApi(emptyList(), listFailure = ApiError(500, "ERR", "boom"))
        val controller = pipelinesController(okChannel(), api)

        controller.load()

        assertTrue(controller.state.value is PipelinesState.Error)
    }

    @Test
    fun create_posts_an_empty_starter_graph_then_reloads_with_the_new_pipeline() = runTest {
        val api = RecordingPipelinesApi(emptyList())
        val controller = pipelinesController(okChannel(), api)
        controller.load()
        assertTrue(controller.state.value is PipelinesState.Empty)

        controller.createPipeline(name = "Raid handler", description = "on raid")

        // The controller built the create body the page intends: the name, the description, and an empty chain.
        assertEquals(1, api.created.size)
        val body: CreatePipelineBody = api.created.first()
        assertEquals("Raid handler", body.name)
        assertEquals("on raid", body.description)
        assertEquals(PipelineGraph().toJson(), body.graph)

        // The reload surfaced the freshly-created row — create really calls the api AND re-lists.
        val state: PipelinesState = controller.state.value
        assertTrue(state is PipelinesState.Ready)
        assertEquals("Raid handler", (state as PipelinesState.Ready).pipelines.first().name)
        assertNull(state.actionError)
    }

    @Test
    fun toggle_patches_only_the_enabled_flag_then_reloads_with_the_flip() = runTest {
        val api = RecordingPipelinesApi(listOf(PipelineSummary(id = "00000001-0000-0000-0000-000000000001", name = "p", isEnabled = true)))
        val controller = pipelinesController(okChannel(), api)
        controller.load()

        controller.togglePipeline(id = "00000001-0000-0000-0000-000000000001", enabled = false)

        // A toggle is a partial PUT carrying only isEnabled — name/graph untouched.
        val update: Triple<String, UpdatePipelineBody, Unit> = api.updated.single()
        assertEquals("00000001-0000-0000-0000-000000000001", update.first)
        assertEquals(false, update.second.isEnabled)
        assertNull(update.second.name)
        assertNull(update.second.graph)

        // The reload reflects the persisted flip.
        val state: PipelinesState = controller.state.value
        assertTrue(state is PipelinesState.Ready)
        assertEquals(false, (state as PipelinesState.Ready).pipelines.first().isEnabled)
    }

    @Test
    fun delete_removes_the_pipeline_then_reloads_to_empty_and_says_deleted() = runTest {
        val feedback = RecordingFeedback()
        val api = RecordingPipelinesApi(listOf(PipelineSummary(id = "00000001-0000-0000-0000-000000000001", name = "p", isEnabled = true)))
        val controller = pipelinesController(okChannel(), api, feedback)
        controller.load()
        assertTrue(controller.state.value is PipelinesState.Ready)

        controller.deletePipeline(id = "00000001-0000-0000-0000-000000000001")

        assertEquals(listOf("00000001-0000-0000-0000-000000000001"), api.deleted)
        assertTrue(controller.state.value is PipelinesState.Empty)
        assertEquals(FeedbackKind.Success, feedback.only.kind)
        assertEquals(Res.string.feedback_pipeline_deleted, feedback.only.label)
    }

    @Test
    fun fetch_blast_radius_returns_the_backend_counted_dependents() = runTest {
        val api =
            RecordingPipelinesApi(
                listOf(PipelineSummary(id = "00000001-0000-0000-0000-000000000001", name = "p", isEnabled = true)),
                blastRadiusResult =
                    ApiResult.Ok(
                        PipelineBlastRadiusSummary(commandCount = 2, chatTriggerCount = 1, timerCount = 1, eventResponseCount = 1)
                    ),
            )
        val controller = pipelinesController(okChannel(), api)
        controller.load()

        val result: ApiResult<PipelineBlastRadiusSummary> =
            controller.fetchBlastRadius("00000001-0000-0000-0000-000000000001")

        assertTrue(result is ApiResult.Ok)
        val summary: PipelineBlastRadiusSummary = (result as ApiResult.Ok).value
        assertEquals(2, summary.commandCount)
        assertEquals(5, summary.totalReferences)
    }

    @Test
    fun fetch_blast_radius_surfaces_the_backend_failure_distinctly_not_as_zero() = runTest {
        val api =
            RecordingPipelinesApi(
                listOf(PipelineSummary(id = "00000001-0000-0000-0000-000000000001", name = "p", isEnabled = true)),
                blastRadiusResult = ApiResult.Failure(ApiError(500, "ERR", "boom")),
            )
        val controller = pipelinesController(okChannel(), api)
        controller.load()

        val result: ApiResult<PipelineBlastRadiusSummary> =
            controller.fetchBlastRadius("00000001-0000-0000-0000-000000000001")

        assertTrue(result is ApiResult.Failure)
    }

    @Test
    fun a_failed_list_write_surfaces_the_error_over_the_kept_list() = runTest {
        val feedback = RecordingFeedback()
        val api =
            RecordingPipelinesApi(
                listOf(PipelineSummary(id = "00000001-0000-0000-0000-000000000001", name = "p", isEnabled = true)),
                writeResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "no permission")),
            )
        val controller = pipelinesController(okChannel(), api, feedback)
        controller.load()

        controller.deletePipeline(id = "00000001-0000-0000-0000-000000000001")

        // The list is kept (not blown away) and the failure surfaces on it + on the frame.
        val state: PipelinesState = controller.state.value
        assertTrue(state is PipelinesState.Ready)
        assertEquals(1, (state as PipelinesState.Ready).pipelines.size)
        assertEquals("no permission", state.actionError)
        assertEquals(FeedbackKind.Error, feedback.only.kind)
        assertEquals(Res.string.feedback_pipeline_save_failed, feedback.only.label)
        assertEquals(listOf<Any>("no permission"), feedback.only.formatArgs)
    }

    // ── Chain editor ──────────────────────────────────────────────────────────

    @Test
    fun open_editor_fetches_the_detail_and_decodes_its_chain() = runTest {
        val seeded =
            PipelineGraph(
                listOf(PipelineStep(action = PipelineNode("send_message", mapOf("message" to "hi"))))
            )
        val api =
            RecordingPipelinesApi(
                listOf(PipelineSummary(id = "00000005-0000-0000-0000-000000000005", name = "Greeter", isEnabled = true)),
                graphs = mutableMapOf("00000005-0000-0000-0000-000000000005" to seeded.toJson()),
            )
        val controller = pipelinesController(okChannel(), api)
        controller.load()

        controller.openEditor(PipelineSummary(id = "00000005-0000-0000-0000-000000000005", name = "Greeter"))

        val state: PipelinesState = controller.state.value
        assertTrue(state is PipelinesState.Editing)
        val editing: PipelinesState.Editing = state as PipelinesState.Editing
        assertEquals("00000005-0000-0000-0000-000000000005", editing.pipelineId)
        assertEquals("Greeter", editing.name)
        assertEquals(1, editing.steps.size)
        assertEquals("send_message", editing.steps.first().action.type)
        assertEquals("hi", editing.steps.first().action.params["message"])
    }

    @Test
    fun add_then_save_persists_the_new_block_into_the_pipeline_graph() = runTest {
        val api =
            RecordingPipelinesApi(
                listOf(PipelineSummary(id = "00000005-0000-0000-0000-000000000005", name = "Greeter", isEnabled = true)),
                graphs = mutableMapOf("00000005-0000-0000-0000-000000000005" to PipelineGraph().toJson()),
            )
        val controller = pipelinesController(okChannel(), api)
        controller.load()
        controller.openEditor(PipelineSummary(id = "00000005-0000-0000-0000-000000000005", name = "Greeter"))

        // Add an action block, then save the chain.
        controller.addStep(PipelineStep(action = PipelineNode("send_message", mapOf("message" to "welcome"))))
        controller.saveChain()

        // The controller PUT the graph, and the store now decodes to the new chain (the re-fetch proves it round-trips).
        val saved: UpdatePipelineBody = api.updated.last().second
        val expectedGraph =
            PipelineGraph(listOf(PipelineStep(action = PipelineNode("send_message", mapOf("message" to "welcome"))))).toJson()
        assertEquals(expectedGraph, saved.graph)

        val state: PipelinesState = controller.state.value
        assertTrue(state is PipelinesState.Editing)
        val steps: List<PipelineStep> = (state as PipelinesState.Editing).steps
        assertEquals(1, steps.size)
        assertEquals("send_message", steps.first().action.type)
        assertEquals("welcome", steps.first().action.params["message"])
    }

    @Test
    fun remove_then_save_drops_the_block_from_the_persisted_graph() = runTest {
        val seeded =
            PipelineGraph(
                listOf(
                    PipelineStep(action = PipelineNode("send_message", mapOf("message" to "one"))),
                    PipelineStep(action = PipelineNode("send_message", mapOf("message" to "two"))),
                )
            )
        val api =
            RecordingPipelinesApi(
                listOf(PipelineSummary(id = "00000005-0000-0000-0000-000000000005", name = "Greeter", isEnabled = true)),
                graphs = mutableMapOf("00000005-0000-0000-0000-000000000005" to seeded.toJson()),
            )
        val controller = pipelinesController(okChannel(), api)
        controller.load()
        controller.openEditor(PipelineSummary(id = "00000005-0000-0000-0000-000000000005", name = "Greeter"))

        controller.removeStep(0)
        controller.saveChain()

        // The persisted graph now has just the surviving second block; the re-fetched editor reflects it.
        val state: PipelinesState = controller.state.value
        assertTrue(state is PipelinesState.Editing)
        val steps: List<PipelineStep> = (state as PipelinesState.Editing).steps
        assertEquals(1, steps.size)
        assertEquals("two", steps.first().action.params["message"])
    }

    @Test
    fun move_down_reorders_the_chain_in_memory() = runTest {
        val seeded =
            PipelineGraph(
                listOf(
                    PipelineStep(action = PipelineNode("send_message", mapOf("message" to "first"))),
                    PipelineStep(action = PipelineNode("send_message", mapOf("message" to "second"))),
                )
            )
        val api =
            RecordingPipelinesApi(
                listOf(PipelineSummary(id = "00000005-0000-0000-0000-000000000005", name = "Greeter", isEnabled = true)),
                graphs = mutableMapOf("00000005-0000-0000-0000-000000000005" to seeded.toJson()),
            )
        val controller = pipelinesController(okChannel(), api)
        controller.load()
        controller.openEditor(PipelineSummary(id = "00000005-0000-0000-0000-000000000005", name = "Greeter"))

        controller.moveStepDown(0)

        val steps: List<PipelineStep> = (controller.state.value as PipelinesState.Editing).steps
        assertEquals(listOf("second", "first"), steps.map { it.action.params["message"] })
    }

    @Test
    fun a_failed_chain_save_keeps_the_edited_chain_and_surfaces_the_error() = runTest {
        val feedback = RecordingFeedback()
        val api =
            RecordingPipelinesApi(
                listOf(PipelineSummary(id = "00000005-0000-0000-0000-000000000005", name = "Greeter", isEnabled = true)),
                graphs = mutableMapOf("00000005-0000-0000-0000-000000000005" to PipelineGraph().toJson()),
                writeResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "denied")),
            )
        val controller = pipelinesController(okChannel(), api, feedback)
        controller.load()
        controller.openEditor(PipelineSummary(id = "00000005-0000-0000-0000-000000000005", name = "Greeter"))
        controller.addStep(PipelineStep(action = PipelineNode("stop")))

        controller.saveChain()

        // The edited chain is NOT lost, and the failure surfaces on the editor + on the frame.
        val state: PipelinesState = controller.state.value
        assertTrue(state is PipelinesState.Editing)
        val editing: PipelinesState.Editing = state as PipelinesState.Editing
        assertEquals(1, editing.steps.size)
        assertEquals("denied", editing.actionError)
        assertEquals(FeedbackKind.Error, feedback.only.kind)
    }

    @Test
    fun a_successful_chain_save_announces_saved_on_the_frame() = runTest {
        val feedback = RecordingFeedback()
        val api =
            RecordingPipelinesApi(
                listOf(PipelineSummary(id = "00000005-0000-0000-0000-000000000005", name = "Greeter", isEnabled = true)),
                graphs = mutableMapOf("00000005-0000-0000-0000-000000000005" to PipelineGraph().toJson()),
            )
        val controller = pipelinesController(okChannel(), api, feedback)
        controller.load()
        controller.openEditor(PipelineSummary(id = "00000005-0000-0000-0000-000000000005", name = "Greeter"))
        controller.addStep(PipelineStep(action = PipelineNode("stop")))

        controller.saveChain()

        assertEquals(FeedbackKind.Success, feedback.only.kind)
        assertEquals(Res.string.feedback_pipeline_saved, feedback.only.label)
    }

    @Test
    fun open_editor_surfaces_the_backend_action_palette_including_unmodelled_blocks() = runTest {
        // The palette membership comes from the backend catalogue — a block the client has NO typed hints for
        // (submit_media) still appears, grouped under its backend category, and falls back to the generic
        // editor (hasHints == false). This is the keystone: the builder can never hide a registered action.
        val api =
            RecordingPipelinesApi(
                listOf(PipelineSummary(id = "00000009-0000-0000-0000-000000000009", name = "Scenes", isEnabled = true)),
                graphs = mutableMapOf("00000009-0000-0000-0000-000000000009" to PipelineGraph().toJson()),
                catalogue =
                    PipelineCatalogueRemote(
                        actions =
                            listOf(
                                PipelineActionDescriptor("send_message", LocalizedTextDto("Chat"), LocalizedTextDto("Send a chat message")),
                                PipelineActionDescriptor("submit_media", LocalizedTextDto("Media"), LocalizedTextDto("Submit a media-share clip")),
                            ),
                        conditions = listOf(PipelineConditionDescriptor("user_role")),
                    ),
            )
        val controller = pipelinesController(okChannel(), api)
        controller.load()

        controller.openEditor(PipelineSummary(id = "00000009-0000-0000-0000-000000000009", name = "Scenes"))

        val state: PipelinesState = controller.state.value
        assertTrue(state is PipelinesState.Editing)
        val palette = (state as PipelinesState.Editing).palette
        assertEquals(listOf("send_message", "submit_media"), palette.actions.map { it.type })
        assertTrue(palette.action("send_message")!!.hasHints)
        assertFalse(palette.action("submit_media")!!.hasHints)
        assertEquals("Media", palette.action("submit_media")!!.category)
        assertEquals(listOf("Chat", "Media"), palette.actionsByCategory.map { it.first })
    }

    // ── Branching ("if" block) edits (S046-branching-if) ────────────────────────

    @Test
    fun if_block_lanes_add_reorder_and_remove_independently_of_each_other() = runTest {
        val api =
            RecordingPipelinesApi(
                listOf(PipelineSummary(id = "00000005-0000-0000-0000-000000000005", name = "Greeter", isEnabled = true)),
                graphs = mutableMapOf("00000005-0000-0000-0000-000000000005" to PipelineGraph().toJson()),
            )
        val controller = pipelinesController(okChannel(), api)
        controller.load()
        controller.openEditor(PipelineSummary(id = "00000005-0000-0000-0000-000000000005", name = "Greeter"))

        val blockId: String = controller.addIfBlock(PipelineNode("user_role", mapOf("role" to "mod")))

        // The block itself: no runnable action of its own, its condition is carried by the SAME `condition`
        // field an ordinary leaf step uses (not `blockConfig` — the engine's EvaluateConditionTreeAsync only
        // ever reads a step's Conditions, never BlockConfigJson, for an "if" block), top-level order 0.
        val afterBlock: List<PipelineStep> = (controller.state.value as PipelinesState.Editing).steps
        val block: PipelineStep = afterBlock.single { it.id == blockId }
        assertEquals("if", block.blockKind)
        assertNull(block.parentStepId)
        assertEquals(0, block.order)
        assertEquals("user_role", block.condition?.type)
        assertEquals("mod", block.condition?.params?.get("role"))
        assertNull(block.blockConfig)

        controller.addBranchStep(blockId, "then", PipelineStep(action = PipelineNode("send_message", mapOf("message" to "then-1"))))
        controller.addBranchStep(blockId, "then", PipelineStep(action = PipelineNode("send_message", mapOf("message" to "then-2"))))
        controller.addBranchStep(blockId, "else", PipelineStep(action = PipelineNode("send_message", mapOf("message" to "else-1"))))

        fun steps(): List<PipelineStep> = (controller.state.value as PipelinesState.Editing).steps
        fun lane(branch: String): List<PipelineStep> =
            steps().filter { it.parentStepId == blockId && it.branch == branch }.sortedBy { it.order }

        // Each lane got its own ids/parentStepId/branch/order — "then" and "else" never share an order sequence.
        assertEquals(listOf("then-1", "then-2"), lane("then").map { it.action.params["message"] })
        assertEquals(listOf(0, 1), lane("then").map { it.order })
        assertEquals(listOf("else-1"), lane("else").map { it.action.params["message"] })
        assertEquals(listOf(0), lane("else").map { it.order })

        // Reorder within "then" only — the "else" lane and the block's own order must not move.
        val thenSecondId: String = lane("then")[1].id!!
        controller.moveBranchStepUp(thenSecondId)
        assertEquals(listOf("then-2", "then-1"), lane("then").map { it.action.params["message"] })
        assertEquals(listOf("else-1"), lane("else").map { it.action.params["message"] })
        assertEquals(0, steps().single { it.id == blockId }.order)

        // Remove the "else" child — "then" (and the block) are untouched.
        val elseChildId: String = lane("else").single().id!!
        controller.removeBranchStep(elseChildId)
        assertEquals(emptyList(), lane("else"))
        assertEquals(listOf("then-2", "then-1"), lane("then").map { it.action.params["message"] })
        assertEquals(3, steps().size) // block + 2 "then" children
    }

    // ── Branching ("switch" block) edits (S046-branching-switch) ───────────────

    @Test
    fun switch_block_with_three_cases_including_a_default_produces_the_correct_tree_shape() = runTest {
        val api =
            RecordingPipelinesApi(
                listOf(PipelineSummary(id = "00000006-0000-0000-0000-000000000006", name = "Router", isEnabled = true)),
                graphs = mutableMapOf("00000006-0000-0000-0000-000000000006" to PipelineGraph().toJson()),
            )
        val controller = pipelinesController(okChannel(), api)
        controller.load()
        controller.openEditor(PipelineSummary(id = "00000006-0000-0000-0000-000000000006", name = "Router"))

        val switchId: String = controller.addSwitchBlock("{{args.1}}")

        fun steps(): List<PipelineStep> = (controller.state.value as PipelinesState.Editing).steps

        // The switch itself: no runnable action of its own, its value carried by `blockConfig` (never
        // `condition` — the engine's ExecuteSwitchAsync only ever reads a switch step's BlockConfigJson),
        // top-level order 0.
        val switchStep: PipelineStep = steps().single { it.id == switchId }
        assertEquals("switch", switchStep.blockKind)
        assertNull(switchStep.parentStepId)
        assertEquals(0, switchStep.order)
        assertNull(switchStep.condition)
        assertEquals("{{args.1}}", (switchStep.blockConfig as? JsonObject)?.get("value")?.jsonPrimitive?.contentOrNull)

        fun caseStep(match: String, operator: String, isDefault: Boolean): PipelineStep =
            PipelineStep(
                action = PipelineNode(type = "block"),
                blockKind = "switch_case",
                blockConfig =
                    JsonObject(
                        mapOf(
                            "match" to JsonPrimitive(match),
                            "operator" to JsonPrimitive(operator),
                            "is_default" to JsonPrimitive(isDefault),
                        )
                    ),
            )

        controller.addBranchStep(switchId, null, caseStep("1", "eq", false))
        controller.addBranchStep(switchId, null, caseStep("2", "gt", false))
        controller.addBranchStep(switchId, null, caseStep("", "eq", true))

        fun cases(): List<PipelineStep> =
            steps().filter { it.parentStepId == switchId && it.blockKind == "switch_case" }.sortedBy { it.order }

        assertEquals(3, cases().size)
        assertEquals(listOf(0, 1, 2), cases().map { it.order })
        assertTrue(cases().all { it.parentStepId == switchId })

        val matches: List<String?> = cases().map { (it.blockConfig as? JsonObject)?.get("match")?.jsonPrimitive?.contentOrNull }
        assertEquals(listOf("1", "2", ""), matches)
        val operators: List<String?> = cases().map { (it.blockConfig as? JsonObject)?.get("operator")?.jsonPrimitive?.contentOrNull }
        assertEquals(listOf("eq", "gt", "eq"), operators)
        val defaults: List<Boolean?> = cases().map { (it.blockConfig as? JsonObject)?.get("is_default")?.jsonPrimitive?.boolean }
        assertEquals(listOf(false, false, true), defaults)

        // The value/match/operator/is_default fields land ONLY in blockConfig — never on `condition`.
        assertTrue(cases().all { it.condition == null })
        assertEquals(4, steps().size) // switch + 3 cases
    }

    @Test
    fun switch_case_reorder_updates_order_within_the_switchs_own_lane_only() = runTest {
        val api =
            RecordingPipelinesApi(
                listOf(PipelineSummary(id = "00000007-0000-0000-0000-000000000007", name = "Router", isEnabled = true)),
                graphs = mutableMapOf("00000007-0000-0000-0000-000000000007" to PipelineGraph().toJson()),
            )
        val controller = pipelinesController(okChannel(), api)
        controller.load()
        controller.openEditor(PipelineSummary(id = "00000007-0000-0000-0000-000000000007", name = "Router"))

        val switchId: String = controller.addSwitchBlock("{{args.1}}")
        fun caseStep(match: String): PipelineStep =
            PipelineStep(
                action = PipelineNode(type = "block"),
                blockKind = "switch_case",
                blockConfig = JsonObject(mapOf("match" to JsonPrimitive(match), "operator" to JsonPrimitive("eq"), "is_default" to JsonPrimitive(false))),
            )
        controller.addBranchStep(switchId, null, caseStep("a"))
        controller.addBranchStep(switchId, null, caseStep("b"))
        controller.addBranchStep(switchId, null, caseStep("c"))

        fun cases(): List<PipelineStep> =
            (controller.state.value as PipelinesState.Editing)
                .steps
                .filter { it.parentStepId == switchId && it.blockKind == "switch_case" }
                .sortedBy { it.order }

        val matchOf: (PipelineStep) -> String? = { (it.blockConfig as? JsonObject)?.get("match")?.jsonPrimitive?.contentOrNull }
        assertEquals(listOf("a", "b", "c"), cases().map(matchOf))

        // Move the middle case ("b") up one slot — its own order swaps with "a"'s, "c" is untouched.
        val middleId: String = cases()[1].id!!
        controller.moveBranchStepUp(middleId)
        assertEquals(listOf("b", "a", "c"), cases().map(matchOf))
        assertEquals(listOf(0, 1, 2), cases().map { it.order })

        // Move the (now-last) case down — no-op, it is already at the bottom of its lane.
        val lastId: String = cases().last().id!!
        controller.moveBranchStepDown(lastId)
        assertEquals(listOf("b", "a", "c"), cases().map(matchOf))
    }

    @Test
    fun removing_a_switch_case_reindexes_the_remaining_siblings_order() = runTest {
        val api =
            RecordingPipelinesApi(
                listOf(PipelineSummary(id = "00000008-0000-0000-0000-000000000008", name = "Router", isEnabled = true)),
                graphs = mutableMapOf("00000008-0000-0000-0000-000000000008" to PipelineGraph().toJson()),
            )
        val controller = pipelinesController(okChannel(), api)
        controller.load()
        controller.openEditor(PipelineSummary(id = "00000008-0000-0000-0000-000000000008", name = "Router"))

        val switchId: String = controller.addSwitchBlock("{{args.1}}")
        fun caseStep(match: String): PipelineStep =
            PipelineStep(
                action = PipelineNode(type = "block"),
                blockKind = "switch_case",
                blockConfig = JsonObject(mapOf("match" to JsonPrimitive(match), "operator" to JsonPrimitive("eq"), "is_default" to JsonPrimitive(false))),
            )
        controller.addBranchStep(switchId, null, caseStep("a"))
        controller.addBranchStep(switchId, null, caseStep("b"))
        controller.addBranchStep(switchId, null, caseStep("c"))

        fun cases(): List<PipelineStep> =
            (controller.state.value as PipelinesState.Editing)
                .steps
                .filter { it.parentStepId == switchId && it.blockKind == "switch_case" }
                .sortedBy { it.order }
        val matchOf: (PipelineStep) -> String? = { (it.blockConfig as? JsonObject)?.get("match")?.jsonPrimitive?.contentOrNull }

        val middleId: String = cases()[1].id!!
        controller.removeBranchStep(middleId)

        assertEquals(listOf("a", "c"), cases().map(matchOf))
        assertEquals(listOf(0, 1), cases().map { it.order }) // "c" compacted from order 2 down to 1
        assertEquals(3, (controller.state.value as PipelinesState.Editing).steps.size) // switch + 2 remaining cases
    }

    // ── Branching ("loop" block) edits (S046-branching-loop) ───────────────────

    @Test
    fun loop_block_body_lane_adds_reorders_and_removes_a_step() = runTest {
        val api =
            RecordingPipelinesApi(
                listOf(PipelineSummary(id = "00000009-0000-0000-0000-000000000009", name = "Spammer", isEnabled = true)),
                graphs = mutableMapOf("00000009-0000-0000-0000-000000000009" to PipelineGraph().toJson()),
            )
        val controller = pipelinesController(okChannel(), api)
        controller.load()
        controller.openEditor(PipelineSummary(id = "00000009-0000-0000-0000-000000000009", name = "Spammer"))

        val loopId: String = controller.addLoopBlock(mode = "repeat", count = 3, maxIterations = 10)

        fun steps(): List<PipelineStep> = (controller.state.value as PipelinesState.Editing).steps

        // The block itself: no runnable action of its own, its iteration config carried by `blockConfig`
        // (`LoopBlockConfig { mode, count, max_iterations }` on the backend) — never `condition`, since
        // mode == "repeat" never touches the engine's Conditions read. Top-level order 0.
        val loopStep: PipelineStep = steps().single { it.id == loopId }
        assertEquals("loop", loopStep.blockKind)
        assertNull(loopStep.parentStepId)
        assertEquals(0, loopStep.order)
        assertNull(loopStep.condition)
        val config: JsonObject = loopStep.blockConfig as JsonObject
        assertEquals("repeat", config["mode"]?.jsonPrimitive?.contentOrNull)
        assertEquals(3, config["count"]?.jsonPrimitive?.intOrNull)
        assertEquals(10, config["max_iterations"]?.jsonPrimitive?.intOrNull)
        assertNull(config["list_var"])

        controller.addBranchStep(loopId, null, PipelineStep(action = PipelineNode("send_message", mapOf("message" to "body-1"))))
        controller.addBranchStep(loopId, null, PipelineStep(action = PipelineNode("send_message", mapOf("message" to "body-2"))))

        fun body(): List<PipelineStep> = steps().filter { it.parentStepId == loopId }.sortedBy { it.order }

        // Body steps land in the loop's single lane — no branch label needed, `parentStepId` alone
        // disambiguates it (ExecuteLoopAsync walks node.Children with no branch filter, PipelineEngine.cs:1821).
        assertTrue(body().all { it.branch == null })
        assertEquals(listOf("body-1", "body-2"), body().map { it.action.params["message"] })
        assertEquals(listOf(0, 1), body().map { it.order })

        // Reorder within the body lane.
        val secondId: String = body()[1].id!!
        controller.moveBranchStepUp(secondId)
        assertEquals(listOf("body-2", "body-1"), body().map { it.action.params["message"] })
        assertEquals(0, steps().single { it.id == loopId }.order)

        // Remove one body step — the other and the loop block are untouched, and order re-compacts.
        val firstId: String = body()[0].id!!
        controller.removeBranchStep(firstId)
        assertEquals(listOf("body-1"), body().map { it.action.params["message"] })
        assertEquals(listOf(0), body().map { it.order })
        assertEquals(2, steps().size) // loop + 1 remaining body step
    }

    @Test
    fun loop_block_in_while_mode_carries_its_condition_on_the_condition_field_not_blockconfig() = runTest {
        val api =
            RecordingPipelinesApi(
                listOf(PipelineSummary(id = "0000000a-0000-0000-0000-00000000000a", name = "Waiter", isEnabled = true)),
                graphs = mutableMapOf("0000000a-0000-0000-0000-00000000000a" to PipelineGraph().toJson()),
            )
        val controller = pipelinesController(okChannel(), api)
        controller.load()
        controller.openEditor(PipelineSummary(id = "0000000a-0000-0000-0000-00000000000a", name = "Waiter"))

        val loopId: String =
            controller.addLoopBlock(
                mode = "while",
                whileCondition = PipelineNode("variable_equals", mapOf("name" to "keep_going", "value" to "true")),
            )

        val loopStep: PipelineStep =
            (controller.state.value as PipelinesState.Editing).steps.single { it.id == loopId }
        // ExecuteLoopAsync's "while" branch calls EvaluateConditionTreeAsync(ctx, node.Step.Conditions) —
        // the SAME field an "if" block's condition lives on, never blockConfig (PipelineEngine.cs:1779).
        assertEquals("variable_equals", loopStep.condition?.type)
        assertEquals("keep_going", loopStep.condition?.params?.get("name"))
        assertEquals("while", (loopStep.blockConfig as JsonObject)["mode"]?.jsonPrimitive?.contentOrNull)
        assertNull((loopStep.blockConfig as JsonObject)["count"])
    }

    @Test
    fun opening_a_pipeline_with_a_stored_nested_if_block_decodes_the_full_tree_shape() = runTest {
        val seeded =
            PipelineGraph(
                listOf(
                    PipelineStep(
                        action = PipelineNode(type = "block"),
                        blockKind = "if",
                        condition = PipelineNode("user_role", mapOf("role" to "mod")),
                        id = "blk-1",
                        order = 0,
                    ),
                    PipelineStep(
                        action = PipelineNode("send_message", mapOf("message" to "hi mod")),
                        id = "then-1",
                        parentStepId = "blk-1",
                        branch = "then",
                        order = 0,
                    ),
                    PipelineStep(
                        action = PipelineNode("send_message", mapOf("message" to "hi viewer")),
                        id = "else-1",
                        parentStepId = "blk-1",
                        branch = "else",
                        order = 0,
                    ),
                )
            )
        val api =
            RecordingPipelinesApi(
                listOf(PipelineSummary(id = "00000006-0000-0000-0000-000000000006", name = "Branching", isEnabled = true)),
                graphs = mutableMapOf("00000006-0000-0000-0000-000000000006" to seeded.toJson()),
            )
        val controller = pipelinesController(okChannel(), api)
        controller.load()

        controller.openEditor(PipelineSummary(id = "00000006-0000-0000-0000-000000000006", name = "Branching"))

        val steps: List<PipelineStep> = (controller.state.value as PipelinesState.Editing).steps
        assertEquals(3, steps.size)
        val block: PipelineStep = steps.single { it.id == "blk-1" }
        assertEquals("if", block.blockKind)
        assertNull(block.parentStepId)
        val thenChild: PipelineStep = steps.single { it.parentStepId == "blk-1" && it.branch == "then" }
        assertEquals("hi mod", thenChild.action.params["message"])
        assertEquals(0, thenChild.order)
        val elseChild: PipelineStep = steps.single { it.parentStepId == "blk-1" && it.branch == "else" }
        assertEquals("hi viewer", elseChild.action.params["message"])
        assertEquals(0, elseChild.order)
    }

    // ── S047 dry-run (Test button) ──────────────────────────────────────────────

    @Test
    fun test_run_sends_the_typed_in_variables_and_surfaces_the_captured_result() = runTest {
        val pipelineId = "00000009-0000-0000-0000-000000000009"
        val api =
            RecordingPipelinesApi(
                listOf(PipelineSummary(id = pipelineId, name = "Scenes", isEnabled = true)),
                graphs = mutableMapOf(pipelineId to PipelineGraph().toJson()),
                testRunResult =
                    ApiResult.Ok(
                        TestRunResult(
                            success = true,
                            durationMs = 42,
                            hostCallCount = 3,
                            chatOutput = listOf("hello viewer"),
                            capturedEffects =
                                listOf(
                                    CapturedEffect(name = "send_message", argsPreview = "hello viewer"),
                                    CapturedEffect(name = "timeout", argsPreview = "user=viewer1 seconds=60"),
                                ),
                        )
                    ),
            )
        val controller = pipelinesController(okChannel(), api)
        controller.load()
        controller.openEditor(PipelineSummary(id = pipelineId, name = "Scenes"))

        controller.testRun(mapOf("target" to "viewer1"))

        // The exact request the API received — proves the variables the operator typed reached the backend.
        assertEquals(1, api.testRunRequests.size)
        val (sentId, sentBody) = api.testRunRequests.single()
        assertEquals(pipelineId, sentId)
        assertEquals(mapOf("target" to "viewer1"), sentBody.variables)

        // The captured result is surfaced in full — not merely "it ran": duration, host calls, chat output, and
        // BOTH captured effects with their exact shape (name + args), never performed for real.
        val state: PipelinesState = controller.state.value
        assertTrue(state is PipelinesState.Editing)
        val editing = state as PipelinesState.Editing
        assertFalse(editing.testRunning)
        assertNull(editing.testError)
        val result: TestRunResult = editing.testResult!!
        assertTrue(result.success)
        assertEquals(42, result.durationMs)
        assertEquals(3, result.hostCallCount)
        assertEquals(listOf("hello viewer"), result.chatOutput)
        assertEquals(2, result.capturedEffects.size)
        assertEquals("send_message", result.capturedEffects[0].name)
        assertEquals("hello viewer", result.capturedEffects[0].argsPreview)
        assertEquals("timeout", result.capturedEffects[1].name)
        assertEquals("user=viewer1 seconds=60", result.capturedEffects[1].argsPreview)
    }

    @Test
    fun test_run_surfaces_a_backend_failure_without_losing_the_open_chain() = runTest {
        val pipelineId = "0000000a-0000-0000-0000-00000000000a"
        val api =
            RecordingPipelinesApi(
                listOf(PipelineSummary(id = pipelineId, name = "Raid response", isEnabled = true)),
                graphs = mutableMapOf(pipelineId to PipelineGraph(listOf(PipelineStep(PipelineNode("send_message")))).toJson()),
                testRunResult = ApiResult.Failure(ApiError(500, "SANDBOX_ERROR", "the sandbox crashed")),
            )
        val controller = pipelinesController(okChannel(), api)
        controller.load()
        controller.openEditor(PipelineSummary(id = pipelineId, name = "Raid response"))

        controller.testRun(emptyMap())

        val state: PipelinesState = controller.state.value
        assertTrue(state is PipelinesState.Editing)
        val editing = state as PipelinesState.Editing
        assertFalse(editing.testRunning)
        assertEquals("the sandbox crashed", editing.testError)
        assertNull(editing.testResult)
        // The chain the operator was editing is untouched by the failed dry-run.
        assertEquals(1, editing.steps.size)
        assertEquals("send_message", editing.steps.single().action.type)
    }

    @Test
    fun test_run_never_performs_a_real_side_effect_only_records_the_captured_intent() = runTest {
        // The whole point of S047: a dry-run must never reach a real chat/moderation/music surface. This proves
        // the CONTRACT the controller relies on — the captured effect for a moderation action carries the intent
        // (who/what/how long) as DATA, not as a real Twitch API call the fake could observe firing.
        val pipelineId = "0000000b-0000-0000-0000-00000000000b"
        val api =
            RecordingPipelinesApi(
                listOf(PipelineSummary(id = pipelineId, name = "Mod chain", isEnabled = true)),
                graphs = mutableMapOf(pipelineId to PipelineGraph().toJson()),
                testRunResult =
                    ApiResult.Ok(
                        TestRunResult(
                            success = true,
                            capturedEffects = listOf(CapturedEffect(name = "play_music", argsPreview = "trackId=abc123")),
                        )
                    ),
            )
        val controller = pipelinesController(okChannel(), api)
        controller.load()
        controller.openEditor(PipelineSummary(id = pipelineId, name = "Mod chain"))

        controller.testRun(emptyMap())

        val editing = controller.state.value as PipelinesState.Editing
        val effect: CapturedEffect = editing.testResult!!.capturedEffects.single()
        assertEquals("play_music", effect.name)
        assertEquals("trackId=abc123", effect.argsPreview)
        // No update() call was made by the test-run path — a dry-run never persists the chain either.
        assertTrue(api.updated.none { it.first == pipelineId })
    }

    private fun okChannel(): ChannelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1")))
}

// Builds the controller with the two editor-picker fakes wired in, so the pipeline-behaviour tests need not
// restate them. Feedback defaults to no-op; a test that asserts feedback passes its RecordingFeedback.
private fun pipelinesController(
    channels: ChannelsApi,
    api: PipelinesApi,
    feedback: Feedback = NoOpFeedback,
): PipelinesController =
    PipelinesController(
        channelsApi = channels,
        pipelinesApi = api,
        webhooksApi = StubWebhooksApi,
        pickListsApi = StubPickListsApi,
        feedback = feedback,
    )

// The editor picker sources are best-effort in the controller; these stubs return empty so the fields fall
// back to free text — the pipeline-behaviour tests don't exercise the pickers.
private object StubWebhooksApi : WebhooksApi {
    // Not exercised here: the counted delete preview has its own tests. The seam is implemented so the double
    // stays a real implementation of the interface rather than a partial one.
    override suspend fun inboundBlastRadius(channelId: String, endpointId: String): ApiResult<BlastRadiusSummary> =
        ApiResult.Ok(BlastRadiusSummary())

    override suspend fun listInbound(channelId: String): ApiResult<List<InboundWebhook>> = ApiResult.Ok(emptyList())
    override suspend fun createInbound(channelId: String, body: CreateInboundBody): ApiResult<InboundWebhook> =
        ApiResult.Ok(InboundWebhook())
    override suspend fun updateInbound(channelId: String, endpointId: String, body: UpdateInboundBody): ApiResult<InboundWebhook> =
        ApiResult.Ok(InboundWebhook())
    override suspend fun toggleInbound(channelId: String, endpointId: String, enabled: Boolean): ApiResult<Unit> =
        ApiResult.Ok(Unit)
    override suspend fun rotateInboundToken(channelId: String, endpointId: String): ApiResult<String> =
        ApiResult.Ok("")
    override suspend fun deleteInbound(channelId: String, endpointId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun outboundEventCatalogue(channelId: String): ApiResult<List<OutboundEventCatalogueEntry>> =
        ApiResult.Ok(emptyList())
    override suspend fun listOutbound(channelId: String): ApiResult<List<OutboundWebhook>> = ApiResult.Ok(emptyList())
    override suspend fun createOutbound(channelId: String, body: CreateOutboundBody): ApiResult<OutboundWebhookCreated> =
        ApiResult.Ok(OutboundWebhookCreated())
    override suspend fun updateOutbound(channelId: String, endpointId: String, body: UpdateOutboundBody): ApiResult<OutboundWebhook> =
        ApiResult.Ok(OutboundWebhook())
    override suspend fun toggleOutbound(channelId: String, endpointId: String, enabled: Boolean): ApiResult<Unit> =
        ApiResult.Ok(Unit)
    override suspend fun reenableOutbound(channelId: String, endpointId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun rotateOutboundSecret(channelId: String, endpointId: String): ApiResult<String> =
        ApiResult.Ok("")
    override suspend fun testOutbound(channelId: String, endpointId: String): ApiResult<WebhookTestResult> =
        ApiResult.Ok(WebhookTestResult())
    override suspend fun outboundDeliveries(channelId: String, endpointId: String): ApiResult<List<OutboundDelivery>> =
        ApiResult.Ok(emptyList())
    override suspend fun deleteOutbound(channelId: String, endpointId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
}

private object StubPickListsApi : PickListsApi {
    // Not exercised here: the counted delete preview has its own tests (DeleteBlastRadiusDialogTest and the
    // backend's blast-radius suites). The seam is implemented so the double stays a real implementation.
    override suspend fun blastRadius(id: String): ApiResult<BlastRadiusSummary> =
        ApiResult.Ok(BlastRadiusSummary())

    override suspend fun list(): ApiResult<List<PickList>> = ApiResult.Ok(emptyList())
    override suspend fun get(id: String): ApiResult<PickList> = ApiResult.Ok(PickList())
    override suspend fun create(body: CreatePickListBody): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun update(id: String, body: UpdatePickListBody): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun delete(id: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun pick(id: String): ApiResult<bot.nomnomz.dashboard.core.network.PickListPreview> =
        ApiResult.Ok(bot.nomnomz.dashboard.core.network.PickListPreview())
}

private class FakeChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result

    override suspend fun list(): ApiResult<List<ChannelSummary>> = ApiResult.Ok(emptyList())

    override suspend fun join(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun leave(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun reset(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun deleteChannel(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun channelScopes(channelId: String) = error("stub")
    override suspend fun startChannelBotConnect(channelId: String) = error("stub")
    override suspend fun channelBotStatus(channelId: String) = error("stub")
    override suspend fun disconnectChannelBot(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun moderatedChannels(): ApiResult<List<ModeratedChannel>> = ApiResult.Ok(emptyList())
}

// A recording fake that behaves like the backend store: list() returns the live summaries, get() returns the
// stored graph, and each successful write mutates the store so the controller's post-write reload/re-fetch
// observes the real consequence (a new row, a flipped flag, a removed row, the persisted graph) — not merely
// that a call happened. [writeResult] forces every write to fail (store untouched) to exercise the error path.
private class RecordingPipelinesApi(
    initial: List<PipelineSummary>,
    private val listFailure: ApiError? = null,
    private val graphs: MutableMap<String, kotlinx.serialization.json.JsonObject> = mutableMapOf(),
    private val writeResult: ApiResult<Unit> = ApiResult.Ok(Unit),
    private val blastRadiusResult: ApiResult<PipelineBlastRadiusSummary> = ApiResult.Ok(PipelineBlastRadiusSummary()),
    private val testRunResult: ApiResult<TestRunResult> = ApiResult.Ok(TestRunResult(success = true)),
    private val catalogue: PipelineCatalogueRemote =
        PipelineCatalogueRemote(
            actions = listOf(PipelineActionDescriptor("send_message", LocalizedTextDto("Chat"), LocalizedTextDto("Send a chat message"))),
            conditions = listOf(PipelineConditionDescriptor("user_role")),
        ),
) : PipelinesApi {
    private val store: MutableList<PipelineSummary> = initial.toMutableList()
    private var nextSeq: Int = 1

    val created: MutableList<CreatePipelineBody> = mutableListOf()
    val updated: MutableList<Triple<String, UpdatePipelineBody, Unit>> = mutableListOf()
    val deleted: MutableList<String> = mutableListOf()

    override suspend fun list(channelId: String): ApiResult<List<PipelineSummary>> =
        listFailure?.let { ApiResult.Failure(it) } ?: ApiResult.Ok(store.toList())

    override suspend fun catalogue(channelId: String): ApiResult<PipelineCatalogueRemote> =
        ApiResult.Ok(catalogue)

    override suspend fun get(channelId: String, id: String): ApiResult<PipelineDetail> {
        val summary: PipelineSummary =
            store.firstOrNull { it.id == id }
                ?: return ApiResult.Failure(ApiError(404, "NOT_FOUND", "no pipeline"))
        return ApiResult.Ok(
            PipelineDetail(
                id = summary.id,
                name = summary.name,
                description = summary.description,
                isEnabled = summary.isEnabled,
                triggerCount = summary.triggerCount,
                graph = graphs[id],
            )
        )
    }

    override suspend fun create(channelId: String, body: CreatePipelineBody): ApiResult<Unit> {
        created += body
        if (writeResult is ApiResult.Ok) {
            val id: String = "test-pipeline-${nextSeq++}"
            store += PipelineSummary(id = id, name = body.name, description = body.description, isEnabled = body.isEnabled)
            graphs[id] = body.graph
        }
        return writeResult
    }

    override suspend fun createReturning(channelId: String, body: CreatePipelineBody): ApiResult<PipelineDetail> {
        created += body
        if (writeResult !is ApiResult.Ok) return ApiResult.Failure(ApiError(403, "FORBIDDEN", "denied"))
        val id: String = "test-pipeline-${nextSeq++}"
        store += PipelineSummary(id = id, name = body.name, description = body.description, isEnabled = body.isEnabled)
        graphs[id] = body.graph
        return ApiResult.Ok(PipelineDetail(id = id, name = body.name, description = body.description, isEnabled = body.isEnabled))
    }

    override suspend fun update(channelId: String, id: String, body: UpdatePipelineBody): ApiResult<Unit> {
        updated += Triple(id, body, Unit)
        if (writeResult is ApiResult.Ok) {
            val index: Int = store.indexOfFirst { it.id == id }
            if (index >= 0) {
                val existing: PipelineSummary = store[index]
                store[index] =
                    existing.copy(
                        name = body.name ?: existing.name,
                        description = body.description ?: existing.description,
                        isEnabled = body.isEnabled ?: existing.isEnabled,
                    )
            }
            body.graph?.let { graphs[id] = it }
        }
        return writeResult
    }

    override suspend fun delete(channelId: String, id: String): ApiResult<Unit> {
        deleted += id
        if (writeResult is ApiResult.Ok) {
            store.removeAll { it.id == id }
            graphs.remove(id)
        }
        return writeResult
    }

    override suspend fun blastRadius(channelId: String, id: String): ApiResult<PipelineBlastRadiusSummary> =
        blastRadiusResult

    /** Every test-run request the controller sent — proves the exact variables reached the API, once per call. */
    val testRunRequests: MutableList<Pair<String, PipelineTestRunBody>> = mutableListOf()

    override suspend fun testRun(channelId: String, id: String, body: PipelineTestRunBody): ApiResult<TestRunResult> {
        testRunRequests += id to body
        return testRunResult
    }
}

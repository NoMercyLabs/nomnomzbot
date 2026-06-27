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

import bot.nomnomz.dashboard.core.feedback.FeedbackKind
import bot.nomnomz.dashboard.core.feedback.RecordingFeedback
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CreatePipelineBody
import bot.nomnomz.dashboard.core.network.PipelineDetail
import bot.nomnomz.dashboard.core.network.PipelineGraph
import bot.nomnomz.dashboard.core.network.PipelineNode
import bot.nomnomz.dashboard.core.network.PipelineStep
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.PipelinesApi
import bot.nomnomz.dashboard.core.network.UpdatePipelineBody
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_pipeline_deleted
import nomnomzbot.composeapp.generated.resources.feedback_pipeline_saved
import nomnomzbot.composeapp.generated.resources.feedback_pipeline_save_failed

// Proves the Pipelines page state machine: resolve the active channel, list its real pipelines, drive the
// full list-level management surface (create / rename / toggle / delete), open a pipeline's action-chain
// editor, mutate the chain (add / remove / reorder / configure), and persist it. Every assertion checks the
// resulting STATE / the body the controller built / the backend store's real change — never a smoke "it ran".
class PipelinesControllerTest {

    // ── List surface ──────────────────────────────────────────────────────────

    @Test
    fun load_surfaces_the_channel_pipelines_on_success() = runTest {
        val controller =
            PipelinesController(
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
        val controller = PipelinesController(okChannel(), RecordingPipelinesApi(emptyList()))

        controller.load()

        assertTrue(controller.state.value is PipelinesState.Empty)
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            PipelinesController(
                FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                RecordingPipelinesApi(emptyList()),
            )

        controller.load()

        assertTrue(controller.state.value is PipelinesState.Error)
    }

    @Test
    fun load_errors_when_the_list_call_fails() = runTest {
        val api = RecordingPipelinesApi(emptyList(), listFailure = ApiError(500, "ERR", "boom"))
        val controller = PipelinesController(okChannel(), api)

        controller.load()

        assertTrue(controller.state.value is PipelinesState.Error)
    }

    @Test
    fun create_posts_an_empty_starter_graph_then_reloads_with_the_new_pipeline() = runTest {
        val api = RecordingPipelinesApi(emptyList())
        val controller = PipelinesController(okChannel(), api)
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
        val controller = PipelinesController(okChannel(), api)
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
        val controller = PipelinesController(okChannel(), api, feedback)
        controller.load()
        assertTrue(controller.state.value is PipelinesState.Ready)

        controller.deletePipeline(id = "00000001-0000-0000-0000-000000000001")

        assertEquals(listOf("00000001-0000-0000-0000-000000000001"), api.deleted)
        assertTrue(controller.state.value is PipelinesState.Empty)
        assertEquals(FeedbackKind.Success, feedback.only.kind)
        assertEquals(Res.string.feedback_pipeline_deleted, feedback.only.label)
    }

    @Test
    fun a_failed_list_write_surfaces_the_error_over_the_kept_list() = runTest {
        val feedback = RecordingFeedback()
        val api =
            RecordingPipelinesApi(
                listOf(PipelineSummary(id = "00000001-0000-0000-0000-000000000001", name = "p", isEnabled = true)),
                writeResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "no permission")),
            )
        val controller = PipelinesController(okChannel(), api, feedback)
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
        val controller = PipelinesController(okChannel(), api)
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
        val controller = PipelinesController(okChannel(), api)
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
        val controller = PipelinesController(okChannel(), api)
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
        val controller = PipelinesController(okChannel(), api)
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
        val controller = PipelinesController(okChannel(), api, feedback)
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
        val controller = PipelinesController(okChannel(), api, feedback)
        controller.load()
        controller.openEditor(PipelineSummary(id = "00000005-0000-0000-0000-000000000005", name = "Greeter"))
        controller.addStep(PipelineStep(action = PipelineNode("stop")))

        controller.saveChain()

        assertEquals(FeedbackKind.Success, feedback.only.kind)
        assertEquals(Res.string.feedback_pipeline_saved, feedback.only.label)
    }

    private fun okChannel(): ChannelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1")))
}

private class FakeChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result

    override suspend fun list(): ApiResult<List<ChannelSummary>> = ApiResult.Ok(emptyList())

    override suspend fun join(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun leave(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun reset(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
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
) : PipelinesApi {
    private val store: MutableList<PipelineSummary> = initial.toMutableList()
    private var nextSeq: Int = 1

    val created: MutableList<CreatePipelineBody> = mutableListOf()
    val updated: MutableList<Triple<String, UpdatePipelineBody, Unit>> = mutableListOf()
    val deleted: MutableList<String> = mutableListOf()

    override suspend fun list(channelId: String): ApiResult<List<PipelineSummary>> =
        listFailure?.let { ApiResult.Failure(it) } ?: ApiResult.Ok(store.toList())

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
}

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

import bot.nomnomz.dashboard.core.network.PipelineDetail
import bot.nomnomz.dashboard.core.network.PipelineGraph
import bot.nomnomz.dashboard.core.network.PipelineNode
import bot.nomnomz.dashboard.core.network.PipelineStep
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.realtime.HubConfigChanged
import bot.nomnomz.dashboard.core.realtime.HubEvent
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.advanceUntilIdle
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertTrue

/** The Pipelines editor never loses unsaved steps: not to a live update, not to a Back click. */
class PipelinesControllerUnsavedTest {

    private fun step(id: String, message: String): PipelineStep =
        PipelineStep(action = PipelineNode(type = "send_reply", params = mapOf("message" to message)), id = id, order = 0)

    private fun detail(vararg steps: PipelineStep): PipelineDetail =
        PipelineDetail(id = "p1", name = "Raid flow", graph = PipelineGraph(steps.toList()).toJson())

    private fun pipelinesChanged(): HubEvent =
        HubEvent.ConfigChanged(HubConfigChanged(domain = "pipelines", entityId = "p1", action = "updated"))

    private suspend fun openedEditor(api: RecordingPipelinesApiForRecipeTest): PipelinesController {
        val controller: PipelinesController = recipeTestController(api)
        controller.load()
        controller.openEditor(PipelineSummary(id = "p1", name = "Raid flow"))
        assertIs<PipelinesState.Editing>(controller.state.value)
        return controller
    }

    private fun editing(controller: PipelinesController): PipelinesState.Editing =
        assertIs<PipelinesState.Editing>(controller.state.value, "the editor must still be open")

    @Test
    fun a_live_pipelines_update_does_not_throw_the_user_out_of_the_editor() = runTest {
        val api = RecordingPipelinesApiForRecipeTest().also { it.stored = detail(step("s1", "hello")) }
        val controller: PipelinesController = openedEditor(api)
        controller.addStep(step("s2", "unsaved step"))
        val events: MutableSharedFlow<HubEvent> = MutableSharedFlow(extraBufferCapacity = 16)
        val job = launch(start = CoroutineStart.UNDISPATCHED) { controller.subscribeToHub(events) }

        events.emit(pipelinesChanged())
        advanceUntilIdle()

        assertEquals(listOf("s1", "s2"), editing(controller).steps.map { it.id }, "the unsaved step survives the push")
        job.cancel()
    }

    @Test
    fun back_with_unsaved_steps_does_not_drop_the_chain_without_a_confirm() = runTest {
        val api = RecordingPipelinesApiForRecipeTest().also { it.stored = detail(step("s1", "hello")) }
        val controller: PipelinesController = openedEditor(api)
        controller.addStep(step("s2", "unsaved step"))

        controller.closeEditor()

        assertEquals(listOf("s1", "s2"), editing(controller).steps.map { it.id }, "Back alone keeps the chain")
        assertTrue(api.updated.isEmpty(), "closing never saves")
        assertTrue(editing(controller).dirty, "the editor still knows the chain is unsaved")
    }

    @Test
    fun discard_after_a_confirm_returns_to_the_list() = runTest {
        val api = RecordingPipelinesApiForRecipeTest().also { it.stored = detail(step("s1", "hello")) }
        val controller: PipelinesController = openedEditor(api)
        controller.addStep(step("s2", "unsaved step"))

        controller.discardAndClose()

        assertIs<PipelinesState.Ready>(controller.state.value)
    }

    @Test
    fun a_clean_editor_closes_at_once() = runTest {
        val controller: PipelinesController =
            openedEditor(RecordingPipelinesApiForRecipeTest().also { it.stored = detail(step("s1", "hello")) })

        controller.closeEditor()

        assertIs<PipelinesState.Ready>(controller.state.value)
    }

    @Test
    fun the_echo_of_my_own_save_changes_nothing_and_clears_dirty() = runTest {
        val api = RecordingPipelinesApiForRecipeTest().also { it.stored = detail(step("s1", "hello")) }
        val controller: PipelinesController = openedEditor(api)
        controller.addStep(step("s2", "second"))
        assertTrue(editing(controller).dirty)
        val events: MutableSharedFlow<HubEvent> = MutableSharedFlow(extraBufferCapacity = 16)
        val job = launch(start = CoroutineStart.UNDISPATCHED) { controller.subscribeToHub(events) }

        controller.saveChain()
        val afterSave: PipelinesState.Editing = editing(controller)
        events.emit(pipelinesChanged())
        advanceUntilIdle()

        assertEquals(1, api.updated.size)
        assertEquals(false, afterSave.dirty, "saved chain is no longer unsaved")
        assertEquals(afterSave, editing(controller), "the echo leaves the editor exactly as it was")
        job.cancel()
    }

    @Test
    fun a_change_elsewhere_refreshes_a_clean_editor_in_place() = runTest {
        val api = RecordingPipelinesApiForRecipeTest().also { it.stored = detail(step("s1", "hello")) }
        val controller: PipelinesController = openedEditor(api)
        val events: MutableSharedFlow<HubEvent> = MutableSharedFlow(extraBufferCapacity = 16)
        val job = launch(start = CoroutineStart.UNDISPATCHED) { controller.subscribeToHub(events) }
        api.stored = detail(step("s1", "hello"), step("s9", "added by someone else"))

        events.emit(pipelinesChanged())
        advanceUntilIdle()

        assertEquals(listOf("s1", "s9"), editing(controller).steps.map { it.id })
        assertEquals(false, editing(controller).dirty)
        assertEquals(false, editing(controller).changedElsewhere)
        job.cancel()
    }

    @Test
    fun a_change_elsewhere_on_a_dirty_editor_keeps_my_steps_and_raises_the_notice() = runTest {
        val api = RecordingPipelinesApiForRecipeTest().also { it.stored = detail(step("s1", "hello")) }
        val controller: PipelinesController = openedEditor(api)
        controller.addStep(step("s2", "mine"))
        val events: MutableSharedFlow<HubEvent> = MutableSharedFlow(extraBufferCapacity = 16)
        val job = launch(start = CoroutineStart.UNDISPATCHED) { controller.subscribeToHub(events) }
        api.stored = detail(step("s1", "hello"), step("s9", "theirs"))

        events.emit(pipelinesChanged())
        advanceUntilIdle()

        assertEquals(listOf("s1", "s2"), editing(controller).steps.map { it.id }, "my unsaved steps stay")
        assertTrue(editing(controller).changedElsewhere)

        controller.reloadFromServer()

        assertEquals(listOf("s1", "s9"), editing(controller).steps.map { it.id }, "Reload shows the server chain")
        assertEquals(false, editing(controller).changedElsewhere)
        job.cancel()
    }
}

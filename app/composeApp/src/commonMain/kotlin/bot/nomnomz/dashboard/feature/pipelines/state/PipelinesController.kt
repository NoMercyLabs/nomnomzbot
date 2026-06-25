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
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
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
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.json.JsonObject
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_pipeline_deleted
import nomnomzbot.composeapp.generated.resources.feedback_pipeline_save_failed
import nomnomzbot.composeapp.generated.resources.feedback_pipeline_saved

// The Pipelines page state-holder (the visual automation engine). Two surfaces in one flow:
//   1. the LIST of the channel's real pipelines (no fabricated rows), with create / rename / toggle / delete;
//   2. the action-chain EDITOR for a selected pipeline — add / remove / reorder / configure the ordered action
//      blocks (with an optional condition + stop-on-match per step), then save the whole chain.
// Every write reloads the affected surface so the screen always reflects the backend's truth: a list write
// re-lists; a chain save re-fetches the pipeline's detail. The screen renders [state]; a retry calls [load].
class PipelinesController(
    private val channelsApi: ChannelsApi,
    private val pipelinesApi: PipelinesApi,
    private val feedback: Feedback = NoOpFeedback,
) {
    private val _state: MutableStateFlow<PipelinesState> = MutableStateFlow(PipelinesState.Loading)

    /** The page render state: loading / list (ready/empty) / editing a pipeline's chain / error. */
    val state: StateFlow<PipelinesState> = _state.asStateFlow()

    // The channel every read/write targets — resolved by [load] and reused so a mutation never re-resolves it.
    private var channelId: String? = null

    /** Resolve the active channel, then list its pipelines. Returns to the list view. */
    suspend fun load() {
        _state.value = PipelinesState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = PipelinesState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id

        loadList(channel.id)
    }

    private suspend fun loadList(channel: String) {
        when (val result: ApiResult<List<PipelineSummary>> = pipelinesApi.list(channel)) {
            is ApiResult.Failure -> _state.value = PipelinesState.Error(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    if (result.value.isEmpty()) PipelinesState.Empty
                    else PipelinesState.Ready(result.value)
        }
    }

    // ── List-level writes ────────────────────────────────────────────────────

    /** Create a pipeline (empty starter chain), then reload the list so the new row appears. */
    suspend fun createPipeline(name: String, description: String?) {
        val channel: String = channelId ?: return failList(NoChannelError)
        val body =
            CreatePipelineBody(
                name = name,
                description = description?.takeIf { it.isNotBlank() },
                graph = PipelineGraph().toJson(),
            )
        afterListWrite(pipelinesApi.create(channel, body))
    }

    /** Rename / re-describe a pipeline, then reload the list. */
    suspend fun renamePipeline(id: String, name: String, description: String?) {
        val channel: String = channelId ?: return failList(NoChannelError)
        afterListWrite(
            pipelinesApi.update(
                channel,
                id,
                UpdatePipelineBody(name = name, description = description ?: ""),
            )
        )
    }

    /** Flip a pipeline's enabled flag via the update endpoint (no dedicated toggle route). Reloads the list. */
    suspend fun togglePipeline(id: String, enabled: Boolean) {
        val channel: String = channelId ?: return failList(NoChannelError)
        afterListWrite(pipelinesApi.update(channel, id, UpdatePipelineBody(isEnabled = enabled)))
    }

    /** Delete a pipeline, then reload the list. */
    suspend fun deletePipeline(id: String) {
        val channel: String = channelId ?: return failList(NoChannelError)
        afterListWrite(pipelinesApi.delete(channel, id), success = Res.string.feedback_pipeline_deleted)
    }

    // ── Open / close the chain editor ────────────────────────────────────────

    /** Open the action-chain editor for [pipeline]: fetch its detail and decode its chain. */
    suspend fun openEditor(pipeline: PipelineSummary) {
        val channel: String = channelId ?: return failList(NoChannelError)
        _state.value = PipelinesState.Loading
        when (val result: ApiResult<PipelineDetail> = pipelinesApi.get(channel, pipeline.id)) {
            is ApiResult.Failure -> _state.value = PipelinesState.Error(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    PipelinesState.Editing(
                        pipelineId = result.value.id,
                        name = result.value.name,
                        steps = result.value.chain.steps,
                    )
        }
    }

    /** Leave the editor and return to the list (discarding any unsaved chain changes). */
    suspend fun closeEditor() {
        val channel: String = channelId ?: return
        loadList(channel)
    }

    // ── Chain edits (operate on the in-memory Editing state; persisted by [saveChain]) ──

    /** Append a new step (its action + optional condition) to the end of the edited chain. */
    fun addStep(step: PipelineStep) = mutateChain { it + step }

    /** Replace the step at [index] with [step] (a re-configure of its action/condition/stop flag). */
    fun updateStep(index: Int, step: PipelineStep) =
        mutateChain { current ->
            if (index !in current.indices) current
            else current.toMutableList().also { it[index] = step }
        }

    /** Remove the step at [index] from the edited chain. */
    fun removeStep(index: Int) =
        mutateChain { current ->
            if (index !in current.indices) current
            else current.toMutableList().also { it.removeAt(index) }
        }

    /** Move the step at [index] one position earlier (no-op at the top). */
    fun moveStepUp(index: Int) =
        mutateChain { current ->
            if (index <= 0 || index >= current.size) current
            else current.toMutableList().also { it.add(index - 1, it.removeAt(index)) }
        }

    /** Move the step at [index] one position later (no-op at the bottom). */
    fun moveStepDown(index: Int) =
        mutateChain { current ->
            if (index < 0 || index >= current.size - 1) current
            else current.toMutableList().also { it.add(index + 1, it.removeAt(index)) }
        }

    /** Persist the edited chain to the backend, then re-fetch the pipeline so the editor shows the saved truth. */
    suspend fun saveChain() {
        val channel: String = channelId ?: return failEdit(NoChannelError)
        val editing: PipelinesState.Editing = _state.value as? PipelinesState.Editing ?: return
        val graph: JsonObject = PipelineGraph(editing.steps).toJson()

        when (
            val result: ApiResult<Unit> =
                pipelinesApi.update(channel, editing.pipelineId, UpdatePipelineBody(graph = graph))
        ) {
            is ApiResult.Failure -> failEdit(result.error.message)
            is ApiResult.Ok -> {
                feedback.success(Res.string.feedback_pipeline_saved)
                // Re-fetch so the editor reflects exactly what was stored (the engine's canonical decode).
                refetchEditing(channel, editing.pipelineId)
            }
        }
    }

    // ── internals ────────────────────────────────────────────────────────────

    private suspend fun refetchEditing(channel: String, id: String) {
        when (val result: ApiResult<PipelineDetail> = pipelinesApi.get(channel, id)) {
            is ApiResult.Failure -> failEdit(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    PipelinesState.Editing(
                        pipelineId = result.value.id,
                        name = result.value.name,
                        steps = result.value.chain.steps,
                    )
        }
    }

    // Apply an in-memory chain transform while editing; a no-op outside the editor.
    private fun mutateChain(transform: (List<PipelineStep>) -> List<PipelineStep>) {
        val editing: PipelinesState.Editing = _state.value as? PipelinesState.Editing ?: return
        _state.value = editing.copy(steps = transform(editing.steps), actionError = null)
    }

    // A list write either re-lists AND announces success, or surfaces its error over the current list without
    // losing it. [success] lets a delete say "Deleted" while the rest default to "Saved".
    private suspend fun afterListWrite(
        result: ApiResult<Unit>,
        success: org.jetbrains.compose.resources.StringResource = Res.string.feedback_pipeline_saved,
    ) {
        when (result) {
            is ApiResult.Ok -> {
                feedback.success(success)
                channelId?.let { loadList(it) }
            }
            is ApiResult.Failure -> failList(result.error.message)
        }
    }

    private fun failList(detail: String) {
        feedback.error(Res.string.feedback_pipeline_save_failed, detail)
        val current: PipelinesState = _state.value
        _state.value =
            if (current is PipelinesState.Ready) current.copy(actionError = detail)
            else PipelinesState.Error(detail)
    }

    private fun failEdit(detail: String) {
        feedback.error(Res.string.feedback_pipeline_save_failed, detail)
        val current: PipelinesState = _state.value
        if (current is PipelinesState.Editing) _state.value = current.copy(actionError = detail)
    }

    private companion object {
        const val NoChannelError: String = "No active channel — reconnect and try again."
    }
}

/** The Pipelines page render state — the list surface and the chain-editor surface. */
sealed interface PipelinesState {
    data object Loading : PipelinesState

    /**
     * The channel's pipelines are listed. [actionError] is non-null only when the last create/rename/toggle/
     * delete failed — the screen surfaces it as a banner while keeping the list rendered.
     */
    data class Ready(val pipelines: List<PipelineSummary>, val actionError: String? = null) :
        PipelinesState

    data object Empty : PipelinesState

    /**
     * Editing one pipeline's action chain: the [pipelineId] the save targets, the pipeline's [name] (shown in
     * the editor header), the ordered [steps] being edited in memory, and an [actionError] when the last save
     * failed (kept over the edited chain so unsaved work is not lost).
     */
    data class Editing(
        val pipelineId: String,
        val name: String,
        val steps: List<PipelineStep>,
        val actionError: String? = null,
    ) : PipelinesState

    data class Error(val detail: String) : PipelinesState
}

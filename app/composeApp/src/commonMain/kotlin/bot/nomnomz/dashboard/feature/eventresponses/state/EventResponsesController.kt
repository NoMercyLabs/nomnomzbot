// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.eventresponses.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CreatePipelineBody
import bot.nomnomz.dashboard.core.network.EventResponse
import bot.nomnomz.dashboard.core.network.EventResponsePreset
import bot.nomnomz.dashboard.core.network.EventResponseSummary
import bot.nomnomz.dashboard.core.network.EventResponsesApi
import bot.nomnomz.dashboard.core.network.PipelineDetail
import bot.nomnomz.dashboard.core.network.PipelineGraph
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.PipelinesApi
import bot.nomnomz.dashboard.core.network.UpdateEventResponseBody
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Event Responses page state-holder: maps Twitch channel events (follow, sub, cheer, raid, stream.online …)
// to a configured reaction (chat message, overlay, pipeline, or none). The list is seeded by the backend on
// channel join with sensible defaults; the Moderator can toggle / edit; an Editor can also upsert or delete.
// Every write re-lists on success so the page always reflects the backend. The screen renders [state]; a retry
// calls [load] again.
class EventResponsesController(
    private val channelsApi: ChannelsApi,
    private val eventResponsesApi: EventResponsesApi,
    private val pipelinesApi: PipelinesApi,
) {
    private val _state: MutableStateFlow<EventResponsesState> =
        MutableStateFlow(EventResponsesState.Loading)

    /** The page render state. */
    val state: StateFlow<EventResponsesState> = _state.asStateFlow()

    private var channelId: String? = null

    /** Resolve the active channel and load its event responses, preset catalog, and pipelines. */
    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch after a mutation keeps
        // the current content on screen (no flash) and swaps it when the new data arrives.
        if (_state.value !is EventResponsesState.Ready) _state.value = EventResponsesState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = EventResponsesState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id

        // The preset catalog (per-event default templates + variables) and the channel's pipelines back the
        // edit dialog's pre-fill and pipeline-binding. Both are best-effort — a failure just disables that aid.
        val presets: Map<String, EventResponsePreset> =
            when (val result: ApiResult<List<EventResponsePreset>> = eventResponsesApi.catalog(channel.id)) {
                is ApiResult.Ok -> result.value.associateBy { it.eventType }
                is ApiResult.Failure -> emptyMap()
            }
        val pipelines: List<PipelineSummary> =
            when (val result: ApiResult<List<PipelineSummary>> = pipelinesApi.list(channel.id)) {
                is ApiResult.Ok -> result.value
                is ApiResult.Failure -> emptyList()
            }

        when (val result: ApiResult<List<EventResponseSummary>> = eventResponsesApi.list(channel.id)) {
            is ApiResult.Failure -> _state.value = EventResponsesState.Error(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    if (result.value.isEmpty()) EventResponsesState.Empty
                    else EventResponsesState.Ready(result.value, presets = presets, pipelines = pipelines)
        }
    }

    /** The stored full config for [eventType], so the edit dialog pre-fills the current message / bound pipeline. */
    suspend fun detail(eventType: String): EventResponse? {
        val channel: String = channelId ?: return null
        return when (val result: ApiResult<EventResponse> = eventResponsesApi.get(channel, eventType)) {
            is ApiResult.Ok -> result.value
            is ApiResult.Failure -> null
        }
    }

    /**
     * Create a new (empty) pipeline named [pipelineName] and bind it to [eventType] as a pipeline response in one
     * step — the "create-and-bind" flow that replaces pasting a pipeline id. Reloads on success so the row shows
     * the binding; the streamer then builds the chain on the Pipelines page.
     */
    suspend fun createPipelineAndBind(eventType: String, pipelineName: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        val created: PipelineDetail =
            when (
                val result: ApiResult<PipelineDetail> =
                    pipelinesApi.createReturning(
                        channel,
                        CreatePipelineBody(name = pipelineName, graph = PipelineGraph().toJson()),
                    )
            ) {
                is ApiResult.Ok -> result.value
                is ApiResult.Failure -> return failWrite(result.error.message)
            }
        afterWrite(
            eventResponsesApi.upsert(
                channel,
                eventType,
                UpdateEventResponseBody(responseType = "pipeline", pipelineId = created.id),
            )
        )
    }

    /** Toggle [isEnabled] on an event response (partial PUT — only the flag changes). */
    suspend fun toggle(eventType: String, enabled: Boolean) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(
            eventResponsesApi.upsert(
                channel,
                eventType,
                UpdateEventResponseBody(isEnabled = enabled),
            )
        )
    }

    /** Upsert the full event-response config for [eventType]. */
    suspend fun save(
        eventType: String,
        responseType: String,
        message: String?,
        pipelineId: String?,
    ) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(
            eventResponsesApi.upsert(
                channel,
                eventType,
                UpdateEventResponseBody(
                    responseType = responseType,
                    message = message?.takeIf { it.isNotBlank() },
                    pipelineId = pipelineId?.takeIf { it.isNotBlank() },
                ),
            )
        )
    }

    /** Delete an event response (restores the seeded default on the next channel join). */
    suspend fun delete(eventType: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(eventResponsesApi.delete(channel, eventType))
    }

    private suspend fun afterWrite(result: ApiResult<*>) {
        when (result) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    private fun failWrite(detail: String) {
        val current: EventResponsesState = _state.value
        _state.value =
            if (current is EventResponsesState.Ready) current.copy(actionError = detail)
            else EventResponsesState.Error(detail)
    }

    private companion object {
        const val NoChannelError: String = "No active channel — reconnect and try again."
    }
}

/** The Event Responses page render state. */
sealed interface EventResponsesState {
    data object Loading : EventResponsesState

    /**
     * Responses are listed. [actionError] is non-null only when the last write failed — the screen surfaces
     * it as a transient banner while keeping the list rendered.
     */
    data class Ready(
        val responses: List<EventResponseSummary>,
        val presets: Map<String, EventResponsePreset> = emptyMap(),
        val pipelines: List<PipelineSummary> = emptyList(),
        val actionError: String? = null,
    ) : EventResponsesState

    data object Empty : EventResponsesState

    data class Error(val detail: String) : EventResponsesState
}

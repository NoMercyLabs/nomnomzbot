// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.chattriggers.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ChatTrigger
import bot.nomnomz.dashboard.core.network.ChatTriggersApi
import bot.nomnomz.dashboard.core.network.CreateChatTriggerBody
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.PipelinesApi
import bot.nomnomz.dashboard.core.network.UpdateChatTriggerBody
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Chat Triggers page's state-holder (frontend-ia.md §3 — the Chat group, beside Commands). Resolves the
// active channel, then lists its real keyword triggers and the channel's pipelines (for the bind-pipeline
// selector) from the backend (no fabricated rows). It drives the page's writes — create / edit / toggle /
// delete — each of which re-lists on success so the screen always reflects the backend's truth. The screen
// renders [state]; a retry / reconnect calls [load] again. Server-side validation errors (a regex must compile;
// a trigger needs a response or a pipeline) surface as the write's [ChatTriggersState.Ready.actionError].
class ChatTriggersController(
    private val channelsApi: ChannelsApi,
    private val chatTriggersApi: ChatTriggersApi,
    private val pipelinesApi: PipelinesApi,
) {
    private val _state: MutableStateFlow<ChatTriggersState> = MutableStateFlow(ChatTriggersState.Loading)

    /** The page render state: loading / ready (with the triggers) / empty / error. */
    val state: StateFlow<ChatTriggersState> = _state.asStateFlow()

    // The channel the writes target — resolved by [load] and reused by every mutation so a write never has to
    // re-resolve the channel. Null until the first successful resolve.
    private var channelId: String? = null

    /** Resolve the active channel, then list its chat triggers and available pipelines. */
    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch after a mutation keeps the current
        // content on screen (no flash) and swaps it when the new data arrives.
        if (_state.value !is ChatTriggersState.Ready) _state.value = ChatTriggersState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = ChatTriggersState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id

        val triggersResult: ApiResult<List<ChatTrigger>> = chatTriggersApi.list(channel.id)
        val pipelinesResult: ApiResult<List<PipelineSummary>> = pipelinesApi.list(channel.id)

        when (triggersResult) {
            is ApiResult.Failure -> {
                _state.value = ChatTriggersState.Error(triggersResult.error.message)
                return
            }
            is ApiResult.Ok -> Unit
        }

        val triggers: List<ChatTrigger> = (triggersResult as ApiResult.Ok).value
        val pipelines: List<PipelineSummary> =
            if (pipelinesResult is ApiResult.Ok) pipelinesResult.value else emptyList()

        _state.value =
            if (triggers.isEmpty()) ChatTriggersState.Empty(pipelines = pipelines)
            else ChatTriggersState.Ready(triggers = triggers, pipelines = pipelines)
    }

    /** Create a trigger, then reload so the new row appears. Surfaces the server-side validation error on failure. */
    suspend fun createTrigger(
        pattern: String,
        matchType: String,
        caseSensitive: Boolean,
        isEnabled: Boolean,
        response: String?,
        pipelineId: String?,
        cooldownSeconds: Int,
        minPermissionLevel: Int,
    ) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(
            chatTriggersApi.create(
                channel,
                CreateChatTriggerBody(
                    pattern = pattern.trim(),
                    matchType = matchType,
                    caseSensitive = caseSensitive,
                    isEnabled = isEnabled,
                    response = response?.takeIf { it.isNotBlank() },
                    pipelineId = pipelineId,
                    cooldownSeconds = cooldownSeconds,
                    minPermissionLevel = minPermissionLevel,
                ),
            )
        )
    }

    /**
     * Edit a trigger, addressed by its [triggerId]. The reaction fields are sent explicitly for the chosen mode:
     * response mode sends the [response] text (and no pipeline change); pipeline mode sends the [pipelineId] and an
     * empty [response] so the old template is cleared. Reloads on success; surfaces the error on failure.
     */
    suspend fun updateTrigger(
        triggerId: String,
        pattern: String,
        matchType: String,
        caseSensitive: Boolean,
        isEnabled: Boolean,
        usePipeline: Boolean,
        response: String,
        pipelineId: String?,
        cooldownSeconds: Int,
        minPermissionLevel: Int,
    ) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(
            chatTriggersApi.update(
                channel,
                triggerId,
                UpdateChatTriggerBody(
                    pattern = pattern.trim(),
                    matchType = matchType,
                    caseSensitive = caseSensitive,
                    isEnabled = isEnabled,
                    // Pipeline mode clears the template (empty string) so only the pipeline fires; response mode sends
                    // the template and leaves the pipeline field null (unchanged) — the common single-mode edit path.
                    response = if (usePipeline) "" else response.trim(),
                    pipelineId = if (usePipeline) pipelineId else null,
                    cooldownSeconds = cooldownSeconds,
                    minPermissionLevel = minPermissionLevel,
                ),
            )
        )
    }

    /** Flip a trigger's enabled flag via the update endpoint (a partial patch carrying only the flag). Reloads. */
    suspend fun toggleTrigger(triggerId: String, enabled: Boolean) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(chatTriggersApi.update(channel, triggerId, UpdateChatTriggerBody(isEnabled = enabled)))
    }

    /** Delete a trigger, addressed by its [triggerId]. Reloads on success. Surfaces the error on failure. */
    suspend fun deleteTrigger(triggerId: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(chatTriggersApi.delete(channel, triggerId))
    }

    // A write either reloads the list (success) or surfaces its error over the current Ready list without losing
    // it (failure) — so a failed create/edit leaves the page intact with the visible server-side reason (a regex
    // that won't compile, a trigger with no reaction).
    private suspend fun afterWrite(result: ApiResult<Unit>) {
        when (result) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    private fun failWrite(detail: String) {
        val current: ChatTriggersState = _state.value
        _state.value =
            when (current) {
                is ChatTriggersState.Ready -> current.copy(actionError = detail)
                is ChatTriggersState.Empty -> current.copy(actionError = detail)
                else -> ChatTriggersState.Error(detail)
            }
    }

    private companion object {
        const val NoChannelError: String = "No active channel — reconnect and try again."
    }
}

/** The Chat Triggers page render state. */
sealed interface ChatTriggersState {
    data object Loading : ChatTriggersState

    /**
     * The channel's triggers are listed. [pipelines] is the channel's pipeline list (for the bind-pipeline selector
     * in the create/edit dialog). [actionError] is non-null only when the last write failed — the screen surfaces it
     * as a transient banner (carrying the server-side validation reason) while keeping the list rendered.
     */
    data class Ready(
        val triggers: List<ChatTrigger>,
        val pipelines: List<PipelineSummary> = emptyList(),
        val actionError: String? = null,
    ) : ChatTriggersState

    /** No triggers yet, but the channel is onboarded. Carries [pipelines] so the create dialog still works. */
    data class Empty(
        val pipelines: List<PipelineSummary> = emptyList(),
        val actionError: String? = null,
    ) : ChatTriggersState

    data class Error(val detail: String) : ChatTriggersState
}

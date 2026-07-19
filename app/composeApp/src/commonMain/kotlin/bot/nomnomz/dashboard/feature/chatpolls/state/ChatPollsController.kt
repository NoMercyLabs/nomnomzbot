// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.chatpolls.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ChatPoll
import bot.nomnomz.dashboard.core.network.ChatPollsApi
import bot.nomnomz.dashboard.core.network.OpenChatPollRequest
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Chat-polls state-holder: resolves the active channel, loads its polls (the open one first, then history),
// and drives open/close. The screen renders [state] and re-loads on a poll tick so the open poll's tallies stay
// live. Open/close hit the backend and reload on success; a failure keeps the current polls and surfaces the
// error on the Ready state. The open poll is the first entry with status "open"; everything else is history.
class ChatPollsController(
    private val channelsApi: ChannelsApi,
    private val chatPollsApi: ChatPollsApi,
) {
    private val _state: MutableStateFlow<ChatPollsState> = MutableStateFlow(ChatPollsState.Loading)

    /** The render state: loading / ready (open poll + history) / error. */
    val state: StateFlow<ChatPollsState> = _state.asStateFlow()

    private var channelId: String? = null

    /** Resolve the active channel, then load its polls. Keeps content on a refresh so the tallies don't flash. */
    suspend fun load() {
        if (_state.value !is ChatPollsState.Ready) _state.value = ChatPollsState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = ChatPollsState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id

        when (val result: ApiResult<List<ChatPoll>> = chatPollsApi.list(channel.id)) {
            is ApiResult.Failure -> {
                val current: ChatPollsState = _state.value
                _state.value =
                    if (current is ChatPollsState.Ready) current.copy(actionError = result.error.message)
                    else ChatPollsState.Error(result.error.message)
            }
            is ApiResult.Ok -> {
                val open: ChatPoll? = result.value.firstOrNull { it.status.equals("open", ignoreCase = true) }
                val history: List<ChatPoll> = result.value.filter { it.id != open?.id }
                _state.value = ChatPollsState.Ready(openPoll = open, history = history)
            }
        }
    }

    /**
     * Open a poll with [question] and [options] (blank options dropped), optionally auto-closing after
     * [durationSeconds] and announcing it in chat ([announce]). Reloads on success; a failure (e.g. a poll
     * already open → 409) surfaces on the Ready state without dropping the current polls.
     */
    /** Returns true when the poll opened; false (with the error surfaced on Ready) otherwise, so the form
     * can keep the operator's typed question/options on a failed open instead of wiping them. */
    suspend fun open(
        question: String,
        options: List<String>,
        durationSeconds: Int?,
        announce: Boolean,
    ): Boolean {
        val channel: String = channelId ?: return false
        val cleanOptions: List<String> = options.map { it.trim() }.filter { it.isNotEmpty() }
        val request = OpenChatPollRequest(
            question = question.trim(),
            options = cleanOptions,
            durationSeconds = durationSeconds,
            announce = announce,
        )
        return when (val result: ApiResult<ChatPoll> = chatPollsApi.open(channel, request)) {
            is ApiResult.Ok -> {
                load()
                true
            }
            is ApiResult.Failure -> {
                surfaceError(result.error.message)
                false
            }
        }
    }

    /** Close the open poll [pollId] now. Reloads on success; a failure surfaces on the Ready state. */
    suspend fun close(pollId: String) {
        val channel: String = channelId ?: return
        when (val result: ApiResult<ChatPoll> = chatPollsApi.close(channel, pollId)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> surfaceError(result.error.message)
        }
    }

    /** Clear the last action error. */
    fun clearError() {
        val current: ChatPollsState = _state.value
        if (current is ChatPollsState.Ready) _state.value = current.copy(actionError = null)
    }

    private fun surfaceError(message: String) {
        val current: ChatPollsState = _state.value
        if (current is ChatPollsState.Ready) _state.value = current.copy(actionError = message)
        else _state.value = ChatPollsState.Error(message)
    }
}

/** The Chat-polls render state. */
sealed interface ChatPollsState {
    data object Loading : ChatPollsState

    /**
     * The currently [openPoll] (null when none is open) plus the closed [history] (newest-first). [actionError]
     * is non-null only when the last open/close failed — the screen shows it as a banner over the intact polls.
     */
    data class Ready(
        val openPoll: ChatPoll? = null,
        val history: List<ChatPoll> = emptyList(),
        val actionError: String? = null,
    ) : ChatPollsState

    data class Error(val detail: String) : ChatPollsState
}

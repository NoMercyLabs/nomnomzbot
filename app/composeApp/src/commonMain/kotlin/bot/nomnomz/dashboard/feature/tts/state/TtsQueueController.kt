// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.tts.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.TtsApi
import bot.nomnomz.dashboard.core.network.TtsQueueEntry
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The state-holder for the TTS moderator approval queue (item 16 P.1a). When "Require moderator approval" is on,
// every TTS utterance is held here until a mod approves it (played) or rejects it (discarded). Resolves the
// active channel, lists the real pending entries (newest-first, no fabricated rows), and drives approve/reject —
// each of which re-lists on success so the panel always reflects the backend's truth. Entries auto-expire after
// ~10 min server-side; a reload drops the expired ones. A live push for new entries is a later enhancement —
// the panel polls via [load] for now.
class TtsQueueController(
    private val channelsApi: ChannelsApi,
    private val ttsApi: TtsApi,
) {
    private val _state: MutableStateFlow<TtsQueueState> = MutableStateFlow(TtsQueueState.Loading)

    /** The panel render state: loading / ready (with the pending entries) / empty / error. */
    val state: StateFlow<TtsQueueState> = _state.asStateFlow()

    private var channelId: String? = null

    /** Resolve the active channel, then list its pending approval entries. */
    suspend fun load() {
        if (_state.value !is TtsQueueState.Ready) _state.value = TtsQueueState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = TtsQueueState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id

        when (val result: ApiResult<List<TtsQueueEntry>> = ttsApi.queue(channel.id)) {
            is ApiResult.Failure -> _state.value = TtsQueueState.Error(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    if (result.value.isEmpty()) TtsQueueState.Empty
                    else TtsQueueState.Ready(result.value)
        }
    }

    /** Approve [entryId] — the backend synthesises and plays it — then reload so it drops out of the queue. */
    suspend fun approve(entryId: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(ttsApi.approveQueueEntry(channel, entryId))
    }

    /** Reject [entryId] — it is discarded, nothing plays — then reload so it drops out of the queue. */
    suspend fun reject(entryId: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(ttsApi.rejectQueueEntry(channel, entryId))
    }

    private suspend fun afterWrite(result: ApiResult<Unit>) {
        when (result) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    private fun failWrite(detail: String) {
        val current: TtsQueueState = _state.value
        _state.value =
            if (current is TtsQueueState.Ready) current.copy(actionError = detail)
            else TtsQueueState.Error(detail)
    }

    private companion object {
        const val NoChannelError: String = "No active channel — reconnect and try again."
    }
}

/** The TTS approval-queue panel render state. */
sealed interface TtsQueueState {
    data object Loading : TtsQueueState

    /**
     * The channel's pending utterances are listed. [actionError] is non-null only when the last approve/reject
     * failed — the panel surfaces it as a transient banner while keeping the list rendered.
     */
    data class Ready(val entries: List<TtsQueueEntry>, val actionError: String? = null) :
        TtsQueueState

    data object Empty : TtsQueueState

    data class Error(val detail: String) : TtsQueueState
}

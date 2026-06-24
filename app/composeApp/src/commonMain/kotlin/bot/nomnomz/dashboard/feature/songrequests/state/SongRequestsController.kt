// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.songrequests.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.QueuedSong
import bot.nomnomz.dashboard.core.network.SongRequestsApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Song Requests page's state-holder — the channel's live music queue, made controllable. Resolves the
// active channel, loads its real song-request queue from the backend (the connected music provider; no
// fabricated tracks), and drives the supported playback controls: skip / pause / resume, plus removing a
// queued song by position. The screen renders [state]; a retry / reconnect calls [load] again. A control hits
// the backend and reloads on success; on failure the queue stays put and the error surfaces on the Ready state.
class SongRequestsController(
    private val channelsApi: ChannelsApi,
    private val songRequestsApi: SongRequestsApi,
) {
    private val _state: MutableStateFlow<SongRequestsState> =
        MutableStateFlow(SongRequestsState.Loading)

    /** The page render state: loading / ready (with the queue) / empty / error. */
    val state: StateFlow<SongRequestsState> = _state.asStateFlow()

    // The channel the loaded queue belongs to, kept so the controls target the same channel without re-resolving.
    private var channelId: String? = null

    /** Resolve the active channel, then load its song-request queue. */
    suspend fun load() {
        _state.value = SongRequestsState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = SongRequestsState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        channelId = channel.id

        when (val result: ApiResult<List<QueuedSong>> = songRequestsApi.queue(channel.id)) {
            is ApiResult.Failure -> _state.value = SongRequestsState.Error(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    if (result.value.isEmpty()) SongRequestsState.Empty
                    else SongRequestsState.Ready(result.value)
        }
    }

    /** Skip the current track. Reloads the queue on success; surfaces the error on the Ready state on failure. */
    suspend fun skip() = control { channel -> songRequestsApi.skip(channel) }

    /** Pause playback. Reloads the queue on success; surfaces the error on the Ready state on failure. */
    suspend fun pause() = control { channel -> songRequestsApi.pause(channel) }

    /** Resume playback. Reloads the queue on success; surfaces the error on the Ready state on failure. */
    suspend fun resume() = control { channel -> songRequestsApi.resume(channel) }

    /**
     * Remove the queued song at [position] (a [QueuedSong.position]). On success the queue reloads so the
     * removed song drops off; on failure the current queue stays put and the error surfaces on the Ready state.
     * The screen gates this destructive action behind a confirmation, so it only runs on a confirmed click.
     */
    suspend fun remove(position: Int) = control { channel -> songRequestsApi.remove(channel, position) }

    // The shared control flow: run [action] against the resolved channel; reload the queue on success, or keep
    // the current list and surface the failure on the Ready state. No channel resolved yet → nothing to control.
    private suspend fun control(action: suspend (channel: String) -> ApiResult<Unit>) {
        val channel: String = channelId ?: return

        when (val result: ApiResult<Unit> = action(channel)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> {
                val current: SongRequestsState = _state.value
                if (current is SongRequestsState.Ready) {
                    _state.value = current.copy(actionError = result.error.message)
                }
            }
        }
    }
}

/** The Song Requests page render state. */
sealed interface SongRequestsState {
    data object Loading : SongRequestsState

    /** The upcoming queue, plus an optional message when the last control failed (the queue is intact). */
    data class Ready(val queue: List<QueuedSong>, val actionError: String? = null) : SongRequestsState

    data object Empty : SongRequestsState

    data class Error(val detail: String) : SongRequestsState
}

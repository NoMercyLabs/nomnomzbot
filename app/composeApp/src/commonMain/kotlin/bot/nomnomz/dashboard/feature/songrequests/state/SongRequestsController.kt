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

// The Song Requests page's state-holder — the channel's live music queue. Resolves the active channel, then
// loads its real song-request queue from the backend (the connected music provider; no fabricated tracks). The
// screen renders [state]; a pull / reconnect / retry calls [load] again. Read-only this slice — no skip/remove.
class SongRequestsController(
    private val channelsApi: ChannelsApi,
    private val songRequestsApi: SongRequestsApi,
) {
    private val _state: MutableStateFlow<SongRequestsState> =
        MutableStateFlow(SongRequestsState.Loading)

    /** The page render state: loading / ready (with the queue) / empty / error. */
    val state: StateFlow<SongRequestsState> = _state.asStateFlow()

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

        when (val result: ApiResult<List<QueuedSong>> = songRequestsApi.queue(channel.id)) {
            is ApiResult.Failure -> _state.value = SongRequestsState.Error(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    if (result.value.isEmpty()) SongRequestsState.Empty
                    else SongRequestsState.Ready(result.value)
        }
    }
}

/** The Song Requests page render state. */
sealed interface SongRequestsState {
    data object Loading : SongRequestsState

    data class Ready(val queue: List<QueuedSong>) : SongRequestsState

    data object Empty : SongRequestsState

    data class Error(val detail: String) : SongRequestsState
}

// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.games.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.GameSummary
import bot.nomnomz.dashboard.core.network.GamesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Games page's state-holder (economy.md §3.5 — the channel's configured mini-games). Resolves the active
// channel, then loads its real game config from the backend (no fabricated games). The screen renders [state]; a
// retry / reconnect calls [load] again.
class GamesController(
    private val channelsApi: ChannelsApi,
    private val gamesApi: GamesApi,
) {
    private val _state: MutableStateFlow<GamesState> = MutableStateFlow(GamesState.Loading)

    /** The page render state: loading / ready (with the games) / empty / error. */
    val state: StateFlow<GamesState> = _state.asStateFlow()

    /** Resolve the active channel, then load its configured games. */
    suspend fun load() {
        _state.value = GamesState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = GamesState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        when (val result: ApiResult<List<GameSummary>> = gamesApi.list(channel.id)) {
            is ApiResult.Failure -> _state.value = GamesState.Error(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    if (result.value.isEmpty()) GamesState.Empty
                    else GamesState.Ready(result.value)
        }
    }
}

/** The Games page render state. */
sealed interface GamesState {
    data object Loading : GamesState

    data class Ready(val games: List<GameSummary>) : GamesState

    data object Empty : GamesState

    data class Error(val detail: String) : GamesState
}

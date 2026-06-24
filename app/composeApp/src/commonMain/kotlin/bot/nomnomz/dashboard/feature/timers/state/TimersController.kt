// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.timers.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.TimerSummary
import bot.nomnomz.dashboard.core.network.TimersApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Timers page's state-holder: resolve the active channel, then load its real scheduled timers from the
// backend (no fabricated rows). The screen renders [state]; a pull / retry calls [load] again. Read-only for
// this slice — listing only.
class TimersController(
    private val channelsApi: ChannelsApi,
    private val timersApi: TimersApi,
) {
    private val _state: MutableStateFlow<TimersState> = MutableStateFlow(TimersState.Loading)

    /** The page render state: loading / ready (with the rows) / empty / error. */
    val state: StateFlow<TimersState> = _state.asStateFlow()

    /** Resolve the active channel, then load its scheduled timers. */
    suspend fun load() {
        _state.value = TimersState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = TimersState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        when (val result: ApiResult<List<TimerSummary>> = timersApi.list(channel.id)) {
            is ApiResult.Failure -> _state.value = TimersState.Error(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    if (result.value.isEmpty()) TimersState.Empty
                    else TimersState.Ready(result.value)
        }
    }
}

/** The Timers page render state. */
sealed interface TimersState {
    data object Loading : TimersState

    data class Ready(val timers: List<TimerSummary>) : TimersState

    data object Empty : TimersState

    data class Error(val detail: String) : TimersState
}

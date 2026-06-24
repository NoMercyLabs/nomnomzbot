// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.commands.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CommandSummary
import bot.nomnomz.dashboard.core.network.CommandsApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Commands page's state-holder (frontend-ia.md §3 — the Chat group). Resolves the active channel, then
// lists its real custom commands from the backend (no fabricated rows). The screen renders [state]; a retry /
// reconnect calls [load] again.
class CommandsController(
    private val channelsApi: ChannelsApi,
    private val commandsApi: CommandsApi,
) {
    private val _state: MutableStateFlow<CommandsState> = MutableStateFlow(CommandsState.Loading)

    /** The page render state: loading / ready (with the commands) / empty / error. */
    val state: StateFlow<CommandsState> = _state.asStateFlow()

    /** Resolve the active channel, then list its commands. */
    suspend fun load() {
        _state.value = CommandsState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = CommandsState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        when (val result: ApiResult<List<CommandSummary>> = commandsApi.list(channel.id)) {
            is ApiResult.Failure -> _state.value = CommandsState.Error(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    if (result.value.isEmpty()) CommandsState.Empty
                    else CommandsState.Ready(result.value)
        }
    }
}

/** The Commands page render state. */
sealed interface CommandsState {
    data object Loading : CommandsState

    data class Ready(val commands: List<CommandSummary>) : CommandsState

    data object Empty : CommandsState

    data class Error(val detail: String) : CommandsState
}

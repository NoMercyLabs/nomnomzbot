// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.shell.state

import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// Owns the channel list + the operator's active channel selection. Lives at the shell level so every
// page controller can call channelsApi.primaryChannel() (which reads the session-pinned id via
// SessionStore.activeChannelId) and get the right result after a switch.
//
// On [load] it fetches the full channel list, sets the first channel as default in SessionStore (if
// not already set — so an in-progress switched selection is preserved on page reload), and exposes
// the list for the shell's channel-picker dropdown. [select] calls SessionStore.switchChannel so the
// change propagates globally without each controller needing a direct reference here.
class ChannelSwitcherController(
    private val channelsApi: ChannelsApi,
    private val sessionStore: SessionStore,
) {
    private val _state: MutableStateFlow<SwitcherState> = MutableStateFlow(SwitcherState.Loading)

    val state: StateFlow<SwitcherState> = _state.asStateFlow()

    /** The currently-active channel id from the session store. */
    val activeChannelId: StateFlow<String?> = sessionStore.activeChannelId

    /** Load the full channel list and pin the first as default (idempotent if already set). */
    suspend fun load() {
        when (val result: ApiResult<List<ChannelSummary>> = channelsApi.list()) {
            is ApiResult.Failure -> _state.value = SwitcherState.Error(result.error.message)
            is ApiResult.Ok -> {
                val channels: List<ChannelSummary> = result.value
                if (channels.isNotEmpty()) {
                    sessionStore.setDefaultChannel(channels.first().id)
                }
                _state.value = SwitcherState.Ready(channels)
            }
        }
    }

    /** Switch the active channel — affects every page controller on its next load. */
    fun select(channelId: String) {
        sessionStore.switchChannel(channelId)
    }
}

sealed interface SwitcherState {
    data object Loading : SwitcherState
    data class Ready(val channels: List<ChannelSummary>) : SwitcherState
    data class Error(val detail: String) : SwitcherState
}

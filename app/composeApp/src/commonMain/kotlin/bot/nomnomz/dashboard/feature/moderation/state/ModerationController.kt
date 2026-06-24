// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.moderation.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BannedUser
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModerationApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Moderation page's state-holder (read-only slice): resolve the active channel, then load its real list
// of currently-banned viewers from the backend (no fabricated entries). The screen renders [state]; a retry
// calls [load] again. Destructive actions (unban/ban) are a later slice — this holder only reads.
class ModerationController(
    private val channelsApi: ChannelsApi,
    private val moderationApi: ModerationApi,
) {
    private val _state: MutableStateFlow<ModerationState> = MutableStateFlow(ModerationState.Loading)

    /** The page render state: loading / ready (with the bans) / empty / error. */
    val state: StateFlow<ModerationState> = _state.asStateFlow()

    /** Resolve the active channel, then load its banned-viewer list. */
    suspend fun load() {
        _state.value = ModerationState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = ModerationState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        when (val result: ApiResult<List<BannedUser>> = moderationApi.bans(channel.id)) {
            is ApiResult.Failure -> _state.value = ModerationState.Error(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    if (result.value.isEmpty()) ModerationState.Empty
                    else ModerationState.Ready(result.value)
        }
    }
}

/** The Moderation page render state. */
sealed interface ModerationState {
    data object Loading : ModerationState

    data class Ready(val bans: List<BannedUser>) : ModerationState

    data object Empty : ModerationState

    data class Error(val detail: String) : ModerationState
}

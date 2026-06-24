// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.community.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CommunityApi
import bot.nomnomz.dashboard.core.network.CommunityMember
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Community page's state-holder (frontend-ia.md §3 — the channel's viewers). Resolves the active channel,
// then loads its real community list from the backend (Twitch API + chat history; no fabricated viewers). The
// screen renders [state]; a pull / reconnect calls [load] again.
class CommunityController(
    private val channelsApi: ChannelsApi,
    private val communityApi: CommunityApi,
) {
    private val _state: MutableStateFlow<CommunityState> = MutableStateFlow(CommunityState.Loading)

    /** The page render state: loading / ready (with the members) / empty / error. */
    val state: StateFlow<CommunityState> = _state.asStateFlow()

    /** Resolve the active channel, then load its community list. */
    suspend fun load() {
        _state.value = CommunityState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = CommunityState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        when (val result: ApiResult<List<CommunityMember>> = communityApi.members(channel.id)) {
            is ApiResult.Failure -> _state.value = CommunityState.Error(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    if (result.value.isEmpty()) CommunityState.Empty
                    else CommunityState.Ready(result.value)
        }
    }
}

/** The Community page render state. */
sealed interface CommunityState {
    data object Loading : CommunityState

    data class Ready(val members: List<CommunityMember>) : CommunityState

    data object Empty : CommunityState

    data class Error(val detail: String) : CommunityState
}

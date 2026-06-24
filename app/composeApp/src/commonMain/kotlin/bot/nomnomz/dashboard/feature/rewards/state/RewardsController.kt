// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.rewards.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.RewardSummary
import bot.nomnomz.dashboard.core.network.RewardsApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Rewards page's state-holder (frontend-ia.md §3 — the channel's channel-point rewards). Resolves the active
// channel, then loads its real reward list from the backend (Twitch Helix Custom Rewards; no fabricated rewards).
// The screen renders [state]; a retry / reconnect calls [load] again. Read-only this slice.
class RewardsController(
    private val channelsApi: ChannelsApi,
    private val rewardsApi: RewardsApi,
) {
    private val _state: MutableStateFlow<RewardsState> = MutableStateFlow(RewardsState.Loading)

    /** The page render state: loading / ready (with the rewards) / empty / error. */
    val state: StateFlow<RewardsState> = _state.asStateFlow()

    /** Resolve the active channel, then load its rewards list. */
    suspend fun load() {
        _state.value = RewardsState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = RewardsState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        when (val result: ApiResult<List<RewardSummary>> = rewardsApi.list(channel.id)) {
            is ApiResult.Failure -> _state.value = RewardsState.Error(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    if (result.value.isEmpty()) RewardsState.Empty
                    else RewardsState.Ready(result.value)
        }
    }
}

/** The Rewards page render state. */
sealed interface RewardsState {
    data object Loading : RewardsState

    data class Ready(val rewards: List<RewardSummary>) : RewardsState

    data object Empty : RewardsState

    data class Error(val detail: String) : RewardsState
}

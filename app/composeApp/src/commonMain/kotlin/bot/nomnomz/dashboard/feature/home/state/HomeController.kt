// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.home.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.DashboardApi
import bot.nomnomz.dashboard.core.network.DashboardStats
import bot.nomnomz.dashboard.core.realtime.DashboardHubClient
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Home page's state-holder (frontend-ia.md §3 — the live channel landing). Resolves the active channel,
// then loads its real snapshot from the backend (no fabricated counts). The screen renders [state]; a pull /
// reconnect calls [load] again.
//
// Real-time: when [hubClient] + [baseUrl] + [accessToken] are supplied, [load] connects the hub after the
// channel resolves so all pages receive live push events for the duration of the shell session. The hub
// client is idempotent on repeated [load] calls (reconnects if closed, no-ops if already live).
class HomeController(
    private val channelsApi: ChannelsApi,
    private val dashboardApi: DashboardApi,
    private val hubClient: DashboardHubClient? = null,
    private val baseUrl: () -> String? = { null },
    private val accessToken: () -> String? = { null },
) {
    private val _state: MutableStateFlow<HomeState> = MutableStateFlow(HomeState.Loading)

    /** The page render state: loading / ready (with the snapshot) / error. */
    val state: StateFlow<HomeState> = _state.asStateFlow()

    /** Resolve the active channel, then load its live snapshot. */
    suspend fun load() {
        _state.value = HomeState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = HomeState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        // Connect the real-time hub now that the channel is resolved — idempotent, so repeated [load]
        // calls (e.g. pull-to-refresh) don't open extra connections.
        val url: String? = baseUrl()
        val token: String? = accessToken()
        if (hubClient != null && url != null && token != null) {
            hubClient.connect(url, token, channel.id)
        }

        when (val result: ApiResult<DashboardStats> = dashboardApi.stats(channel.id)) {
            is ApiResult.Failure -> _state.value = HomeState.Error(result.error.message)
            is ApiResult.Ok -> _state.value = HomeState.Ready(result.value)
        }
    }
}

/** The Home page render state. */
sealed interface HomeState {
    data object Loading : HomeState

    data class Ready(val stats: DashboardStats) : HomeState

    data class Error(val detail: String) : HomeState
}

// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.alerts.state

import bot.nomnomz.dashboard.core.network.AlertQueueDto
import bot.nomnomz.dashboard.core.network.AlertQueueEntryDto
import bot.nomnomz.dashboard.core.network.AlertsApi
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The alert-queue tab's state-holder (frontend-ia.md, Event Responses): the channel's ONE cross-platform alert
// queue (widgets-overlays.md section 1.2), read live from the backend. The event-response rows themselves live
// on the Event Responses page, not here. A retry / reconnect calls [load] again.
class AlertsController(
    private val channelsApi: ChannelsApi,
    private val alertsApi: AlertsApi,
) {
    private val _queueState: MutableStateFlow<AlertQueueUiState> =
        MutableStateFlow(AlertQueueUiState.Loading)

    /** The queue render state: loading / ready (with the overlay presence and entries) / error. */
    val queueState: StateFlow<AlertQueueUiState> = _queueState.asStateFlow()

    /** Resolve the active channel, then read its alert queue. */
    suspend fun load() {
        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _queueState.value = AlertQueueUiState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        when (val result: ApiResult<AlertQueueDto> = alertsApi.queue(channel.id)) {
            is ApiResult.Failure -> _queueState.value = AlertQueueUiState.Error(result.error.message)
            is ApiResult.Ok ->
                _queueState.value =
                    AlertQueueUiState.Ready(
                        overlayConnected = result.value.overlayConnected,
                        entries = result.value.entries,
                    )
        }
    }
}

/**
 * The live alert queue's render state (widgets-overlays.md §1.2). [Ready.overlayConnected] must drive the
 * "not connected" banner honestly — it is read live from the backend's overlay presence registry, never
 * derived from whether [Ready.entries] is non-empty (a queue can be healthy and populated while genuinely no
 * OBS browser source is attached).
 */
sealed interface AlertQueueUiState {
    data object Loading : AlertQueueUiState

    data class Ready(val overlayConnected: Boolean, val entries: List<AlertQueueEntryDto>) :
        AlertQueueUiState

    data class Error(val detail: String) : AlertQueueUiState
}

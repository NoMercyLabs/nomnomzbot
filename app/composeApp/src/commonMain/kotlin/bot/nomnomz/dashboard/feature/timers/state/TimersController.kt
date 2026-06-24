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
import bot.nomnomz.dashboard.core.network.CreateTimerRequest
import bot.nomnomz.dashboard.core.network.TimerSummary
import bot.nomnomz.dashboard.core.network.TimersApi
import bot.nomnomz.dashboard.core.network.UpdateTimerRequest
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Timers page's state-holder: resolve the active channel, then load its real scheduled timers from the
// backend (no fabricated rows), and drive the full create / edit / toggle / delete management surface. The
// screen renders [state] for the list and [writeError] for a failed mutation; every successful write reloads
// the list so the screen always reflects the backend's truth.
class TimersController(
    private val channelsApi: ChannelsApi,
    private val timersApi: TimersApi,
) {
    private val _state: MutableStateFlow<TimersState> = MutableStateFlow(TimersState.Loading)

    /** The page render state: loading / ready (with the rows) / empty / error. */
    val state: StateFlow<TimersState> = _state.asStateFlow()

    private val _writeError: MutableStateFlow<String?> = MutableStateFlow(null)

    /** The message from the last failed create / edit / toggle / delete, or null when there is none. */
    val writeError: StateFlow<String?> = _writeError.asStateFlow()

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

    /** Dismiss the current write-error banner (e.g. after the user reads it). */
    fun clearWriteError() {
        _writeError.value = null
    }

    /** Create a new timer from the dialog's fields, then reload the list on success. */
    suspend fun createTimer(name: String, message: String, intervalMinutes: Int, enabled: Boolean) {
        val channelId: String = resolveChannelId() ?: return
        val request =
            CreateTimerRequest(
                name = name,
                messages = listOf(message),
                intervalMinutes = intervalMinutes,
                isEnabled = enabled,
            )
        runWrite { timersApi.create(channelId, request) }
    }

    /** Update an existing timer with the dialog's fields, then reload the list on success. */
    suspend fun updateTimer(
        id: Int,
        name: String,
        message: String,
        intervalMinutes: Int,
        enabled: Boolean,
    ) {
        val channelId: String = resolveChannelId() ?: return
        val request =
            UpdateTimerRequest(
                name = name,
                messages = listOf(message),
                intervalMinutes = intervalMinutes,
                isEnabled = enabled,
            )
        runWrite { timersApi.update(channelId, id, request) }
    }

    /** Delete a timer, then reload the list on success. */
    suspend fun deleteTimer(id: Int) {
        val channelId: String = resolveChannelId() ?: return
        runWrite { timersApi.delete(channelId, id) }
    }

    /**
     * Flip a timer's enabled state. The backend toggle endpoint flips the stored value server-side, so the
     * [enabled] the row clicked from is informational; the reload reflects the new truth either way.
     */
    suspend fun toggleTimer(id: Int, enabled: Boolean) {
        val channelId: String = resolveChannelId() ?: return
        runWrite { timersApi.toggle(channelId, id) }
    }

    // Resolve the active channel for a write; surface a write error (not a full-page error) if none resolves,
    // so the list the user is looking at stays put.
    private suspend fun resolveChannelId(): String? =
        when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
            is ApiResult.Failure -> {
                _writeError.value = result.error.message
                null
            }
            is ApiResult.Ok -> result.value.id
        }

    // Run a mutation: on success clear any prior error and reload the list; on failure surface the message and
    // leave the current list untouched.
    private suspend fun runWrite(write: suspend () -> ApiResult<Unit>) {
        when (val result: ApiResult<Unit> = write()) {
            is ApiResult.Failure -> _writeError.value = result.error.message
            is ApiResult.Ok -> {
                _writeError.value = null
                load()
            }
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

// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.tts.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.TtsApi
import bot.nomnomz.dashboard.core.network.TtsConfig
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The TTS page's state-holder: resolves the active channel, then loads its real TTS configuration from the
// backend (no fabricated values). The screen renders [state] read-only; a retry / reconnect calls [load] again.
class TtsController(
    private val channelsApi: ChannelsApi,
    private val ttsApi: TtsApi,
) {
    private val _state: MutableStateFlow<TtsState> = MutableStateFlow(TtsState.Loading)

    /** The page render state: loading / ready (with the config) / error. */
    val state: StateFlow<TtsState> = _state.asStateFlow()

    /** Resolve the active channel, then load its TTS configuration. */
    suspend fun load() {
        _state.value = TtsState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = TtsState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        when (val result: ApiResult<TtsConfig> = ttsApi.config(channel.id)) {
            is ApiResult.Failure -> _state.value = TtsState.Error(result.error.message)
            is ApiResult.Ok -> _state.value = TtsState.Ready(result.value)
        }
    }
}

/** The TTS page render state. */
sealed interface TtsState {
    data object Loading : TtsState

    data class Ready(val config: TtsConfig) : TtsState

    data class Error(val detail: String) : TtsState
}

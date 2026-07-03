// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.features.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.FeatureStatus
import bot.nomnomz.dashboard.core.network.FeaturesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Features page's state-holder — resolves the active channel, loads its feature flags from the backend,
// and drives the toggle write. Screens render [state]; retries call [load]; [toggle] flips one flag
// and reloads on success so the list always reflects the backend's truth.
class FeaturesController(
    private val channelsApi: ChannelsApi,
    private val featuresApi: FeaturesApi,
) {
    private val _state: MutableStateFlow<FeaturesState> = MutableStateFlow(FeaturesState.Loading)

    /** The page render state: loading / ready (with the feature list) / empty / error. */
    val state: StateFlow<FeaturesState> = _state.asStateFlow()

    private var channelId: String? = null

    /** Resolve the active channel, then list its feature flags. */
    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch after a mutation keeps
        // the current content on screen (no flash) and swaps it when the new data arrives.
        if (_state.value !is FeaturesState.Ready) _state.value = FeaturesState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = FeaturesState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id

        when (val result: ApiResult<List<FeatureStatus>> = featuresApi.list(channel.id)) {
            is ApiResult.Failure -> _state.value = FeaturesState.Error(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    if (result.value.isEmpty()) FeaturesState.Empty
                    else FeaturesState.Ready(result.value)
        }
    }

    /** Toggle the [featureKey] flag. Reloads on success; surfaces the error on failure. */
    suspend fun toggle(featureKey: String) {
        val channel: String = channelId ?: run {
            val current: FeaturesState = _state.value
            if (current is FeaturesState.Ready) {
                _state.value = current.copy(actionError = "No active channel — reconnect and try again.")
            }
            return
        }
        when (val result: ApiResult<Unit> = featuresApi.toggle(channel, featureKey)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> {
                val current: FeaturesState = _state.value
                if (current is FeaturesState.Ready) {
                    _state.value = current.copy(actionError = result.error.message)
                }
            }
        }
    }
}

/** The Features page render state. */
sealed interface FeaturesState {
    data object Loading : FeaturesState

    /**
     * The channel's feature flags. [actionError] is non-null only when the last toggle failed — the list stays
     * rendered so the operator can see which flags are set while the error is surfaced.
     */
    data class Ready(val features: List<FeatureStatus>, val actionError: String? = null) : FeaturesState

    data object Empty : FeaturesState

    data class Error(val detail: String) : FeaturesState
}

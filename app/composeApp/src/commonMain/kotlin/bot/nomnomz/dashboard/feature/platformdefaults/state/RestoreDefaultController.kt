// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.platformdefaults.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.PlatformDefaultPreview
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * One "Restore default" confirm: first loads what a restore would change (so the dialog can name it before
 * anything is written), then restores on confirm. [load] and [restore] are the row's two backend calls.
 */
class RestoreDefaultController(
    private val load: suspend () -> ApiResult<PlatformDefaultPreview>,
    private val restore: suspend () -> ApiResult<PlatformDefaultPreview>,
) {
    private val _state: MutableStateFlow<RestoreDefaultState> = MutableStateFlow(RestoreDefaultState.Loading)
    val state: StateFlow<RestoreDefaultState> = _state.asStateFlow()

    suspend fun open() {
        _state.value = RestoreDefaultState.Loading
        _state.value =
            when (val result: ApiResult<PlatformDefaultPreview> = load()) {
                is ApiResult.Ok -> RestoreDefaultState.Ready(result.value)
                is ApiResult.Failure -> RestoreDefaultState.Failed(result.error.message)
            }
    }

    /** Restores; true once the backend confirms the row matches the default again. */
    suspend fun confirm(): Boolean {
        val current: RestoreDefaultState.Ready = _state.value as? RestoreDefaultState.Ready ?: return false
        _state.value = current.copy(restoring = true)
        return when (val result: ApiResult<PlatformDefaultPreview> = restore()) {
            is ApiResult.Ok -> {
                _state.value = RestoreDefaultState.Ready(result.value)
                true
            }
            is ApiResult.Failure -> {
                _state.value = RestoreDefaultState.Failed(result.error.message)
                false
            }
        }
    }
}

sealed interface RestoreDefaultState {
    data object Loading : RestoreDefaultState

    data class Ready(val preview: PlatformDefaultPreview, val restoring: Boolean = false) : RestoreDefaultState

    data class Failed(val detail: String) : RestoreDefaultState
}

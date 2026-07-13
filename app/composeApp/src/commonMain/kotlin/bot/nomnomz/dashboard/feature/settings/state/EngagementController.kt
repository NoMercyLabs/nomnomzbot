// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.settings.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.EngagementApi
import bot.nomnomz.dashboard.core.network.EngagementConfig
import bot.nomnomz.dashboard.core.network.UpdateEngagementConfigBody
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Settings "Engagement triggers" card's state-holder: loads the channel's real config and persists edits,
// adopting the backend's echoed (validated) config rather than the values typed — so a rejected write (e.g. a
// negative cooldown) never leaves a wrong value on screen. Tenant-resolved via the X-Channel-Id header, so it
// needs only the [EngagementApi] — no channel id to thread.
class EngagementController(private val api: EngagementApi) {
    private val _state: MutableStateFlow<EngagementState> = MutableStateFlow(EngagementState.Loading)

    /** The card render state: loading / ready (the config) / error. */
    val state: StateFlow<EngagementState> = _state.asStateFlow()

    /** Load the channel's engagement config. */
    suspend fun load() {
        if (_state.value !is EngagementState.Ready) _state.value = EngagementState.Loading

        _state.value =
            when (val result: ApiResult<EngagementConfig> = api.getConfig()) {
                is ApiResult.Failure -> EngagementState.Error(result.error.message)
                is ApiResult.Ok -> EngagementState.Ready(config = result.value)
            }
    }

    /**
     * Persist the whole config ([body]); the card adopts the backend's echoed config (the server clamps/
     * validates, so its truth wins). A failure keeps the current Ready state and surfaces the error, so the
     * user's in-progress edit is not lost. A no-op when no config is loaded yet.
     */
    suspend fun save(body: UpdateEngagementConfigBody) {
        val current: EngagementState = _state.value
        if (current !is EngagementState.Ready) return

        _state.value = current.copy(saving = true, justSaved = false, saveError = null)

        _state.value =
            when (val result: ApiResult<EngagementConfig> = api.setConfig(body)) {
                is ApiResult.Failure ->
                    current.copy(saving = false, justSaved = false, saveError = result.error.message)
                is ApiResult.Ok -> EngagementState.Ready(config = result.value, justSaved = true)
            }
    }
}

/** The "Engagement triggers" card render state. */
sealed interface EngagementState {
    data object Loading : EngagementState

    /**
     * The loaded [config], plus the in-flight save signals: [saving] while a write is pending, [justSaved]
     * right after a successful save (the confirmation), and [saveError] when the last save failed. The screen
     * seeds its editable form from [config].
     */
    data class Ready(
        val config: EngagementConfig,
        val saving: Boolean = false,
        val justSaved: Boolean = false,
        val saveError: String? = null,
    ) : EngagementState

    data class Error(val detail: String) : EngagementState
}

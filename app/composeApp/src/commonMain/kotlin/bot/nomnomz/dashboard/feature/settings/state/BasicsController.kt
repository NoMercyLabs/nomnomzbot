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
import bot.nomnomz.dashboard.core.network.ChannelBasics
import bot.nomnomz.dashboard.core.network.ChannelSettingsApi
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.UpdateBasicsBody
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Settings "Bot basics" card's state-holder: resolves the active channel, loads its command prefix,
// default locale, auto-join, and timezone (all real, from the backend — never fabricated), and persists
// edits back, adopting the server's echoed values rather than the requested ones. Mirrors
// [PersonalityController]'s (channelsApi + settingsApi) shape; the resolved channel id is cached from [load]
// so [save] reuses it.
class BasicsController(
    private val channelsApi: ChannelsApi,
    private val settingsApi: ChannelSettingsApi,
) {
    private val _state: MutableStateFlow<BasicsState> = MutableStateFlow(BasicsState.Loading)

    /** The card render state: loading / ready (the loaded basics) / error. */
    val state: StateFlow<BasicsState> = _state.asStateFlow()

    /** The channel resolved by the last successful [load]; [save] targets it without re-resolving. */
    private var channelId: String? = null

    /** Resolve the active channel, then load its basics. */
    suspend fun load() {
        // Only show the full loading state on first load; a refetch after a save keeps the current content.
        if (_state.value !is BasicsState.Ready) _state.value = BasicsState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = BasicsState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id

        _state.value =
            when (val result: ApiResult<ChannelBasics> = settingsApi.getBasics(channel.id)) {
                is ApiResult.Failure -> BasicsState.Error(result.error.message)
                is ApiResult.Ok -> BasicsState.Ready(loaded = result.value)
            }
    }

    /**
     * Persist the edited basics. Nothing on screen changes until the backend echoes the saved values (adopted
     * verbatim — the server validates and is the source of truth), so a rejected write never leaves a wrong
     * value shown. A no-op when no channel is loaded yet.
     */
    suspend fun save(body: UpdateBasicsBody) {
        val target: String = channelId ?: return
        val current: BasicsState = _state.value
        if (current !is BasicsState.Ready) return

        _state.value = current.copy(saving = true, justSaved = false, saveError = null)

        _state.value =
            when (val result: ApiResult<ChannelBasics> = settingsApi.updateBasics(target, body)) {
                is ApiResult.Failure ->
                    current.copy(saving = false, justSaved = false, saveError = result.error.message)
                is ApiResult.Ok ->
                    current.copy(
                        loaded = result.value,
                        saving = false,
                        justSaved = true,
                        saveError = null,
                    )
            }
    }
}

/** The "Bot basics" card render state. */
sealed interface BasicsState {
    data object Loading : BasicsState

    /**
     * The [loaded] basics (the saved baseline the form re-seeds from), plus the in-flight save signals:
     * [saving] while a write is pending, [justSaved] right after a successful save (the confirmation), and
     * [saveError] when the last save failed.
     */
    data class Ready(
        val loaded: bot.nomnomz.dashboard.core.network.ChannelBasics,
        val saving: Boolean = false,
        val justSaved: Boolean = false,
        val saveError: String? = null,
    ) : BasicsState

    data class Error(val detail: String) : BasicsState
}

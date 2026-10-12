// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.streaminfo.state

import bot.nomnomz.dashboard.core.designsystem.component.PickerOption
import bot.nomnomz.dashboard.core.designsystem.resolveRowLabel
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.Category
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.StreamApi
import bot.nomnomz.dashboard.core.network.StreamInfo
import bot.nomnomz.dashboard.core.network.StreamInfoUpdate
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Stream page "Info and presets" tab state-holder: resolves the active channel, loads its real stream info
// (title, category, tags + live context), and persists edits to the broadcast metadata back (no fabricated
// values). The screen renders [state]; it edits a local form seeded from the loaded info and calls [save] to
// write the editable fields through. A retry / reconnect calls [load] again. The resolved channel id is cached
// from [load] so [save] reuses it without re-resolving.
class StreamInfoController(
    private val channelsApi: ChannelsApi,
    private val streamApi: StreamApi,
) {
    private val _state: MutableStateFlow<StreamInfoState> = MutableStateFlow(StreamInfoState.Loading)

    /** The tab render state: loading / ready (with the stream info) / error. */
    val state: StateFlow<StreamInfoState> = _state.asStateFlow()

    /** The channel resolved by the last successful [load]; [save] targets it without re-resolving. */
    private var channelId: String? = null

    /** Resolve the active channel, then load its stream info. */
    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch keeps the current content on screen.
        if (_state.value !is StreamInfoState.Ready) _state.value = StreamInfoState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = StreamInfoState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        channelId = channel.id

        when (val result: ApiResult<StreamInfo> = streamApi.info(channel.id)) {
            is ApiResult.Failure -> _state.value = StreamInfoState.Error(result.error.message)
            is ApiResult.Ok -> _state.value = StreamInfoState.Ready(result.value)
        }
    }

    /**
     * Persist the editable broadcast metadata ([title], [gameName], [tags]) for the loaded channel. The backend
     * resolves the game name to a Twitch game id, pushes the change through Helix (a real but non-destructive
     * write), and echoes the saved values back; the controller adopts THAT echo, not the values the user typed
     * ([StreamInfoState.Ready.justSaved] flags the confirmation). A failure surfaces on the current Ready state
     * without discarding the in-progress edit. No-ops when no channel is loaded yet (the form is only shown once
     * Ready). The read-only live context (isLive, viewerCount, language) is never written.
     */
    suspend fun save(title: String, gameName: String, tags: List<String>) {
        val target: String = channelId ?: return
        val current: StreamInfoState = _state.value
        if (current !is StreamInfoState.Ready) return

        _state.value = current.copy(saving = true, justSaved = false, saveError = null)

        val update: StreamInfoUpdate = StreamInfoUpdate(title = title, gameName = gameName, tags = tags)

        _state.value =
            when (val result: ApiResult<StreamInfo> = streamApi.update(target, update)) {
                is ApiResult.Failure ->
                    current.copy(saving = false, justSaved = false, saveError = result.error.message)
                is ApiResult.Ok -> StreamInfoState.Ready(info = result.value, justSaved = true)
            }
    }

    /**
     * Autocomplete Twitch categories for the editor's category picker. Each match maps to a [PickerOption] whose
     * id is the Twitch category id and label the canonical game name — the stream update writes only the NAME.
     * Best-effort: empty on failure or before the channel resolves.
     */
    suspend fun searchCategories(query: String): List<PickerOption> {
        val target: String = channelId ?: return emptyList()
        return when (val result: ApiResult<List<Category>> = streamApi.searchCategories(target, query)) {
            is ApiResult.Ok -> result.value.map { PickerOption(id = it.id, label = it.name) }
            is ApiResult.Failure -> emptyList()
        }
    }
}

/** The Stream page "Info and presets" tab render state. */
sealed interface StreamInfoState {
    data object Loading : StreamInfoState

    /**
     * The loaded stream info plus the in-flight save signals: [saving] while a write is pending, [justSaved]
     * right after a successful save (the "Saved" confirmation), and [saveError] when the last save failed. The
     * screen seeds its editable form from [info].
     */
    data class Ready(
        val info: StreamInfo,
        val saving: Boolean = false,
        val justSaved: Boolean = false,
        val saveError: String? = null,
    ) : StreamInfoState

    data class Error(val detail: String) : StreamInfoState
}

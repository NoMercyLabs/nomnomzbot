// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.customevents.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.CustomDataSource
import bot.nomnomz.dashboard.core.network.CustomDataSourcePreset
import bot.nomnomz.dashboard.core.network.CustomEventsApi
import bot.nomnomz.dashboard.core.network.UpsertCustomDataSourceBody
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Custom Events page's state-holder: loads the channel's custom data sources and the available quick-start
// presets. All mutations — create, update, delete, test — reload the source list on success so the page always
// reflects the backend's truth. The controller knows nothing about the UI; it exposes only typed state + suspend
// functions for the screen to call from a coroutineScope.
class CustomEventsController(private val api: CustomEventsApi) {

    private val _state: MutableStateFlow<CustomEventsState> = MutableStateFlow(CustomEventsState.Loading)

    /** The current page render state. */
    val state: StateFlow<CustomEventsState> = _state.asStateFlow()

    /** Load (or reload) both the source list and the preset catalogue. */
    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch after a mutation keeps
        // the current content on screen (no flash) and swaps it when the new data arrives.
        if (_state.value !is CustomEventsState.Ready) _state.value = CustomEventsState.Loading

        val sources: List<CustomDataSource> =
            when (val result: ApiResult<List<CustomDataSource>> = api.list()) {
                is ApiResult.Failure -> {
                    _state.value = CustomEventsState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        val presets: List<CustomDataSourcePreset> =
            when (val result: ApiResult<List<CustomDataSourcePreset>> = api.listPresets()) {
                is ApiResult.Failure -> {
                    _state.value = CustomEventsState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        _state.value = CustomEventsState.Ready(sources = sources, presets = presets)
    }

    /** Create a new data source. Returns the created [CustomDataSource] on success, null on failure. */
    suspend fun create(body: UpsertCustomDataSourceBody): CustomDataSource? {
        return when (val result: ApiResult<CustomDataSource> = api.create(body)) {
            is ApiResult.Ok -> {
                load()
                result.value
            }
            is ApiResult.Failure -> {
                failWrite(result.error.message)
                null
            }
        }
    }

    /** Update an existing data source by [id]. Returns the updated source on success, null on failure. */
    suspend fun update(id: String, body: UpsertCustomDataSourceBody): CustomDataSource? {
        return when (val result: ApiResult<CustomDataSource> = api.update(id, body)) {
            is ApiResult.Ok -> {
                load()
                result.value
            }
            is ApiResult.Failure -> {
                failWrite(result.error.message)
                null
            }
        }
    }

    /** Soft-delete a data source. Reloads on success. */
    suspend fun delete(id: String) {
        when (val result: ApiResult<Unit> = api.delete(id)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    /**
     * Fire a sample payload through the source's ingest pipeline. Returns true on success so the UI can
     * show a confirmation; returns false and surfaces an error on failure.
     */
    suspend fun test(id: String, samplePayload: String): Boolean {
        return when (val result: ApiResult<Unit> = api.test(id, samplePayload)) {
            is ApiResult.Ok -> true
            is ApiResult.Failure -> {
                failWrite(result.error.message)
                false
            }
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private fun failWrite(detail: String) {
        val current: CustomEventsState = _state.value
        _state.value =
            if (current is CustomEventsState.Ready) current.copy(actionError = detail)
            else CustomEventsState.Error(detail)
    }
}

/** The Custom Events page render state. */
sealed interface CustomEventsState {
    data object Loading : CustomEventsState

    data class Ready(
        val sources: List<CustomDataSource>,
        val presets: List<CustomDataSourcePreset>,
        val actionError: String? = null,
    ) : CustomEventsState

    data class Error(val detail: String) : CustomEventsState
}

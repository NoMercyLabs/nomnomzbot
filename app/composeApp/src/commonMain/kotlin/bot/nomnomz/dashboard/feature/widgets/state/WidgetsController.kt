// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.widgets.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.WidgetSummary
import bot.nomnomz.dashboard.core.network.WidgetsApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Overlays page's state-holder (frontend-ia.md §3 — the Stream group; a plain holder, not a ViewModel).
// Resolves the active channel, then lists its real OBS overlay widgets from the backend (no fabricated rows) —
// each row carries the browser-source URL the operator copies into OBS. It also drives the page's writes —
// enable/disable and delete — each of which re-lists on success so the screen always reflects the backend's
// truth. Delete is destructive (the overlay's URL stops resolving once it is gone), so the screen confirms it
// before calling [deleteWidget]. The screen renders [state]; a retry / reconnect calls [load] again.
class WidgetsController(
    private val channelsApi: ChannelsApi,
    private val widgetsApi: WidgetsApi,
) {
    private val _state: MutableStateFlow<WidgetsState> = MutableStateFlow(WidgetsState.Loading)

    /** The page render state: loading / ready (with the overlays) / empty / error. */
    val state: StateFlow<WidgetsState> = _state.asStateFlow()

    // The channel the writes target — resolved by [load] and reused by every mutation so a write never has to
    // re-resolve the channel. Null until the first successful resolve.
    private var channelId: String? = null

    /** Resolve the active channel, then list its overlay widgets. */
    suspend fun load() {
        _state.value = WidgetsState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = WidgetsState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id

        when (val result: ApiResult<List<WidgetSummary>> = widgetsApi.list(channel.id)) {
            is ApiResult.Failure -> _state.value = WidgetsState.Error(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    if (result.value.isEmpty()) WidgetsState.Empty
                    else WidgetsState.Ready(result.value)
        }
    }

    /** Flip a widget's enabled flag (partial PUT carrying only the flag), then reload on success. */
    suspend fun toggleWidget(widgetId: String, enabled: Boolean) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(widgetsApi.setEnabled(channel, widgetId, enabled))
    }

    /**
     * Delete a widget, addressed by its [widgetId]. Reloads on success. Surfaces the error on failure.
     * Destructive — the screen routes this through the confirm dialog before calling it (deleting the
     * overlay invalidates its browser-source URL).
     */
    suspend fun deleteWidget(widgetId: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(widgetsApi.delete(channel, widgetId))
    }

    // A write either reloads the list (success) or surfaces its error over the current Ready list without
    // losing it (failure) — so a failed toggle/delete leaves the page intact with a visible reason.
    private suspend fun afterWrite(result: ApiResult<Unit>) {
        when (result) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    private fun failWrite(detail: String) {
        val current: WidgetsState = _state.value
        _state.value =
            if (current is WidgetsState.Ready) current.copy(actionError = detail)
            else WidgetsState.Error(detail)
    }

    private companion object {
        const val NoChannelError: String = "No active channel — reconnect and try again."
    }
}

/** The Overlays page render state. */
sealed interface WidgetsState {
    data object Loading : WidgetsState

    /**
     * The channel's overlay widgets are listed. [actionError] is non-null only when the last toggle/delete
     * failed — the screen surfaces it as a transient banner while keeping the list rendered.
     */
    data class Ready(val widgets: List<WidgetSummary>, val actionError: String? = null) :
        WidgetsState

    data object Empty : WidgetsState

    data class Error(val detail: String) : WidgetsState
}

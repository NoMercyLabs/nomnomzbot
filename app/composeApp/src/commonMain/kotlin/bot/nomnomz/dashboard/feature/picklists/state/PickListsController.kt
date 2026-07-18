// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.picklists.state

import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.CreatePickListBody
import bot.nomnomz.dashboard.core.network.PickList
import bot.nomnomz.dashboard.core.network.PickListPreview
import bot.nomnomz.dashboard.core.network.PickListsApi
import bot.nomnomz.dashboard.core.network.UpdatePickListBody
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_picklist_deleted
import nomnomzbot.composeapp.generated.resources.feedback_picklist_save_failed
import nomnomzbot.composeapp.generated.resources.feedback_picklist_saved

// The Pick Lists page's state-holder (frontend-ia.md §3 — the Chat group). Lists the channel's real named
// pick-lists from the backend (no fabricated rows) — the generic primitive behind the `{list.pick.<name>}`
// template variable. The pick-lists routes resolve the channel from the request, so this controller needs no
// channel resolve step — it talks straight to the pick-lists facade. It drives the page's writes — create /
// edit / delete — each of which re-lists on success so the screen always reflects the backend's truth. The
// screen renders [state]; a retry / reconnect calls [load] again.
class PickListsController(
    private val pickListsApi: PickListsApi,
    private val feedback: Feedback = NoOpFeedback,
) {
    private val _state: MutableStateFlow<PickListsState> = MutableStateFlow(PickListsState.Loading)

    /** The page render state: loading / ready (with the lists) / empty / error. */
    val state: StateFlow<PickListsState> = _state.asStateFlow()

    // The last "Test" draw the operator ran, shown in a small dialog: null = no dialog, a value = show it. The
    // name is carried so the dialog can title itself; the pick is the drawn entry (or an error message on failure).
    private val _preview: MutableStateFlow<PickListPreviewResult?> = MutableStateFlow(null)

    /** The pending "Test" result dialog: null when closed, a value when a draw (or its error) should be shown. */
    val preview: StateFlow<PickListPreviewResult?> = _preview.asStateFlow()

    /** List the channel's pick-lists. */
    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch after a mutation keeps
        // the current content on screen (no flash) and swaps it when the new data arrives.
        if (_state.value !is PickListsState.Ready) _state.value = PickListsState.Loading

        when (val result: ApiResult<List<PickList>> = pickListsApi.list()) {
            is ApiResult.Failure -> _state.value = PickListsState.Error(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    if (result.value.isEmpty()) PickListsState.Empty
                    else PickListsState.Ready(result.value)
        }
    }

    /** Create a pick-list, then reload so the new row appears. Surfaces the error on failure. */
    suspend fun createPickList(name: String, description: String?, items: List<String>) {
        afterWrite(
            pickListsApi.create(
                CreatePickListBody(name.trim(), description.orNullIfBlank(), items.cleaned()),
            )
        )
    }

    /**
     * Edit a pick-list's name/description/items, addressed by its [id]. The [items] list fully replaces the stored
     * entries. Reloads on success. Surfaces the error on failure.
     */
    suspend fun updatePickList(id: String, name: String, description: String?, items: List<String>) {
        afterWrite(
            pickListsApi.update(
                id,
                UpdatePickListBody(name.trim(), description.orNullIfBlank(), items.cleaned()),
            )
        )
    }

    /** Delete a pick-list, addressed by its [id]. Reloads on success. Surfaces the error on failure. */
    suspend fun deletePickList(id: String) {
        afterWrite(pickListsApi.delete(id), success = Res.string.feedback_picklist_deleted)
    }

    /**
     * Draw a random sample from [id] (named [name] for the dialog title) and open the "Test" result dialog — the
     * same pick `{list.pick.<name>}` performs, so the operator can see what a viewer would get. On failure the
     * dialog shows the reason instead of a draw.
     */
    suspend fun previewPickList(id: String, name: String) {
        _preview.value =
            when (val result: ApiResult<PickListPreview> = pickListsApi.pick(id)) {
                is ApiResult.Ok -> PickListPreviewResult(name = name, pick = result.value.pick, error = null)
                is ApiResult.Failure -> PickListPreviewResult(name = name, pick = null, error = result.error.message)
            }
    }

    /** Dismiss the "Test" result dialog. */
    fun dismissPreview() {
        _preview.value = null
    }

    // A write either reloads the list AND announces success on the frame, or surfaces its error over the
    // current Ready list without losing it (failure) — so a failed edit/delete leaves the page intact with a
    // visible reason AND a frame-level error message. [success] lets a delete say "Deleted" while the rest
    // default to "Saved".
    private suspend fun afterWrite(
        result: ApiResult<Unit>,
        success: org.jetbrains.compose.resources.StringResource = Res.string.feedback_picklist_saved,
    ) {
        when (result) {
            is ApiResult.Ok -> {
                feedback.success(success)
                load()
            }
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    private fun failWrite(detail: String) {
        // Announce the failure on the frame (persistent until dismissed) AND keep the in-page banner.
        feedback.error(Res.string.feedback_picklist_save_failed, detail)
        val current: PickListsState = _state.value
        _state.value =
            if (current is PickListsState.Ready) current.copy(actionError = detail)
            else PickListsState.Error(detail)
    }

    // The description is sent as null (omitted from the wire body) when the operator leaves it blank — an empty
    // string is not a meaningful description.
    private fun String?.orNullIfBlank(): String? = this?.takeIf { it.isNotBlank() }

    // The entries are trimmed and blank ones dropped before they go over the wire — a blank line the operator
    // left in the editor is not a pickable entry, so it never reaches the stored list.
    private fun List<String>.cleaned(): List<String> = mapNotNull { it.trim().takeIf(String::isNotBlank) }
}

/**
 * One "Test" draw result for the dialog: the list [name] (dialog title), the drawn [pick] (null on failure), and
 * the [error] reason (null on success). Exactly one of [pick] / [error] is set.
 */
data class PickListPreviewResult(
    val name: String,
    val pick: String?,
    val error: String?,
)

/** The Pick Lists page render state. */
sealed interface PickListsState {
    data object Loading : PickListsState

    /**
     * The channel's pick-lists are listed. [actionError] is non-null only when the last create/edit/delete failed —
     * the screen surfaces it as a transient banner while keeping the list rendered.
     */
    data class Ready(val lists: List<PickList>, val actionError: String? = null) : PickListsState

    data object Empty : PickListsState

    data class Error(val detail: String) : PickListsState
}

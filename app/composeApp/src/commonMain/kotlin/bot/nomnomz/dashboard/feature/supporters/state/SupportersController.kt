// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.supporters.state

import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.SupporterConnection
import bot.nomnomz.dashboard.core.network.SupporterEvent
import bot.nomnomz.dashboard.core.network.SupporterEventsPage
import bot.nomnomz.dashboard.core.network.SupportersApi
import bot.nomnomz.dashboard.core.network.UpsertSupporterConnectionBody
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_supporter_connection_removed
import nomnomzbot.composeapp.generated.resources.feedback_supporter_connection_saved
import nomnomzbot.composeapp.generated.resources.feedback_supporter_save_failed
import org.jetbrains.compose.resources.StringResource

// The Supporters page's state-holder (supporter-events.md §5, item 13 slice 13a — the channel's monetization
// connections + recorded supporter events). Lists the broadcaster's REAL connections and events from the backend
// (no fabricated rows) and drives the connection writes: connect (upsert enabled), toggle the enforced
// enable-flag, and disconnect. The supporters routes resolve the channel from the request, so this controller
// needs no channel resolve step — it talks straight to the supporters facade.
//
// Two independent surfaces, two render states the screen projects: [connections] (the provider tiles' backing
// rows — a Broadcaster wires these) and [events] (the paged, filterable supporter feed everyone with read can
// watch). Every connection write re-lists on success so the screen always reflects the backend's truth; the event
// feed is a pure read, paged + filtered by the screen through [loadEvents].
class SupportersController(
    private val supportersApi: SupportersApi,
    private val feedback: Feedback = NoOpFeedback,
) {
    private val _connections: MutableStateFlow<ConnectionsState> =
        MutableStateFlow(ConnectionsState.Loading)

    /** The connections render state: loading / ready (with the backend's connection rows) / error. */
    val connections: StateFlow<ConnectionsState> = _connections.asStateFlow()

    private val _events: MutableStateFlow<EventsState> = MutableStateFlow(EventsState.Loading)

    /** The supporter-event feed render state: loading / ready (a page) / empty (no events) / error. */
    val events: StateFlow<EventsState> = _events.asStateFlow()

    // ── Connections ────────────────────────────────────────────────────────────────

    /** List the broadcaster's supporter connections (empty is a valid Ready — the tiles still render). */
    suspend fun loadConnections() {
        // Only show the full loading state on first load; a refetch after a write keeps the current tiles on
        // screen (no flash) and swaps them when the new data arrives.
        if (_connections.value !is ConnectionsState.Ready) _connections.value = ConnectionsState.Loading

        when (val result: ApiResult<List<SupporterConnection>> = supportersApi.connections()) {
            is ApiResult.Failure -> _connections.value = ConnectionsState.Error(result.error.message)
            is ApiResult.Ok -> _connections.value = ConnectionsState.Ready(result.value)
        }
    }

    /**
     * Create/update the [sourceKey] connection with the given enabled state (backend PUT upsert). Used both to
     * CONNECT a provider (enabled = true, passing the provider's verification [authSecret] — the backend then
     * auto-provisions the inbound ingest endpoint from it in one step and returns its URL on the connection) and
     * to flip its enforced enable-toggle (no secret — [authSecret] null leaves the stored one untouched).
     * Reloads on success; surfaces the error over the kept tiles on failure.
     */
    suspend fun upsertConnection(
        sourceKey: String,
        connectionMode: String,
        isEnabled: Boolean,
        authSecret: String? = null,
    ) {
        val body = UpsertSupporterConnectionBody(
            sourceKey = sourceKey,
            connectionMode = connectionMode,
            authSecret = authSecret?.takeIf { it.isNotBlank() },
            isEnabled = isEnabled,
        )
        afterConnectionWrite(supportersApi.upsertConnection(body))
    }

    /**
     * Disconnect the [sourceKey] provider (backend DELETE) — stops ingest for that money source. Reloads on
     * success; surfaces the error over the kept tiles on failure.
     */
    suspend fun disconnect(sourceKey: String) {
        afterConnectionWrite(
            supportersApi.deleteConnection(sourceKey),
            success = Res.string.feedback_supporter_connection_removed,
        )
    }

    // A connection write either reloads the tiles AND announces success on the frame, or surfaces its error over
    // the current Ready tiles without losing them (failure). [success] lets a disconnect say "removed" while the
    // rest default to "updated".
    private suspend fun afterConnectionWrite(
        result: ApiResult<Unit>,
        success: StringResource = Res.string.feedback_supporter_connection_saved,
    ) {
        when (result) {
            is ApiResult.Ok -> {
                feedback.success(success)
                loadConnections()
            }
            is ApiResult.Failure -> failConnectionWrite(result.error.message)
        }
    }

    private fun failConnectionWrite(detail: String) {
        feedback.error(Res.string.feedback_supporter_save_failed, detail)
        val current: ConnectionsState = _connections.value
        _connections.value =
            if (current is ConnectionsState.Ready) current.copy(actionError = detail)
            else ConnectionsState.Error(detail)
    }

    // ── Events feed ──────────────────────────────────────────────────────────────

    /**
     * Load one [page] (1-based) of the supporter feed, optionally filtered by [kind] / [sourceKey] (null = no
     * filter). The screen owns the filter + page selection and calls this on any change. An unfiltered empty first
     * page is the true "no events yet" [EventsState.Empty]; a filtered empty page stays [EventsState.Ready] (with
     * an empty list) so the filter bar remains visible to clear it.
     */
    suspend fun loadEvents(page: Int = 1, kind: String? = null, sourceKey: String? = null) {
        if (_events.value !is EventsState.Ready) _events.value = EventsState.Loading

        when (
            val result: ApiResult<SupporterEventsPage> =
                supportersApi.events(page = page, take = PageSize, kind = kind, sourceKey = sourceKey)
        ) {
            is ApiResult.Failure -> _events.value = EventsState.Error(result.error.message)
            is ApiResult.Ok -> {
                val unfilteredFirstPage: Boolean = page == 1 && kind == null && sourceKey == null
                _events.value =
                    if (result.value.data.isEmpty() && unfilteredFirstPage) EventsState.Empty
                    else EventsState.Ready(result.value.data, page = page, hasMore = result.value.hasMore)
            }
        }
    }

    companion object {
        /** The event-feed page size sent as the backend `take` query param. */
        const val PageSize: Int = 25
    }
}

/** The Supporters connections render state. */
sealed interface ConnectionsState {
    data object Loading : ConnectionsState

    /**
     * The broadcaster's connection rows (possibly empty — the provider tiles still render, showing "not
     * connected"). [actionError] is non-null only when the last connect/disconnect/toggle failed — the screen
     * surfaces it as a transient banner while keeping the tiles rendered.
     */
    data class Ready(val connections: List<SupporterConnection>, val actionError: String? = null) :
        ConnectionsState

    data class Error(val detail: String) : ConnectionsState
}

/** The supporter-event feed render state. */
sealed interface EventsState {
    data object Loading : EventsState

    /**
     * One page of the feed. [page] is the 1-based page shown; [hasMore] tells the pager whether a "Next" page
     * exists. The list can be empty when a filter matches nothing (the screen keeps the filter bar so it can be
     * cleared).
     */
    data class Ready(val events: List<SupporterEvent>, val page: Int, val hasMore: Boolean) : EventsState

    /** No supporter events exist at all (unfiltered first page came back empty) — the true first-run state. */
    data object Empty : EventsState

    data class Error(val detail: String) : EventsState
}

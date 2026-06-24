// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.alerts.state

import bot.nomnomz.dashboard.core.network.AlertDetail
import bot.nomnomz.dashboard.core.network.AlertSummary
import bot.nomnomz.dashboard.core.network.AlertsApi
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.UpdateAlertBody
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Alerts page's state-holder (frontend-ia.md — the Community group): what the bot says/does when a channel
// event fires (a follow, sub, raid, cheer, …). Resolves the active channel, then lists its real configured
// event responses from the backend (no fabricated rows). It also drives the page's writes — create / edit /
// toggle / delete — each of which re-lists on success so the screen always reflects the backend's truth. The
// screen renders [state]; a retry / reconnect calls [load] again.
class AlertsController(
    private val channelsApi: ChannelsApi,
    private val alertsApi: AlertsApi,
) {
    private val _state: MutableStateFlow<AlertsState> = MutableStateFlow(AlertsState.Loading)

    /** The page render state: loading / ready (with the event responses) / empty / error. */
    val state: StateFlow<AlertsState> = _state.asStateFlow()

    // The channel the writes target — resolved by [load] and reused by every mutation so a write never has to
    // re-resolve the channel. Null until the first successful resolve.
    private var channelId: String? = null

    /** Resolve the active channel, then list its event responses. */
    suspend fun load() {
        _state.value = AlertsState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = AlertsState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id

        when (val result: ApiResult<List<AlertSummary>> = alertsApi.list(channel.id)) {
            is ApiResult.Failure -> _state.value = AlertsState.Error(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    if (result.value.isEmpty()) AlertsState.Empty
                    else AlertsState.Ready(result.value)
        }
    }

    /**
     * The full configuration for one [eventType] — the edit dialog reads this to pre-fill the message (the
     * list item carries no message body). Returns null on failure, surfacing the error over the kept list so
     * the screen can stay put rather than open a blank editor.
     */
    suspend fun detail(eventType: String): AlertDetail? {
        val channel: String = channelId ?: run {
            failWrite(NoChannelError)
            return null
        }
        return when (val result: ApiResult<AlertDetail> = alertsApi.detail(channel, eventType)) {
            is ApiResult.Ok -> result.value
            is ApiResult.Failure -> {
                failWrite(result.error.message)
                null
            }
        }
    }

    /**
     * Create (or replace) the response for [eventType] with a chat [message], then reload so the new row
     * appears. Surfaces the error on failure.
     */
    suspend fun createAlert(eventType: String, message: String, isEnabled: Boolean) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(
            alertsApi.upsert(
                channel,
                eventType,
                UpdateAlertBody(
                    isEnabled = isEnabled,
                    responseType = ChatMessageResponseType,
                    message = message,
                ),
            )
        )
    }

    /**
     * Edit an existing response's [message] (and enabled flag), addressed by its [eventType]. Reloads on
     * success. Surfaces the error on failure.
     */
    suspend fun updateAlert(eventType: String, message: String, isEnabled: Boolean) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(
            alertsApi.upsert(
                channel,
                eventType,
                UpdateAlertBody(isEnabled = isEnabled, message = message),
            )
        )
    }

    /** Flip a response's enabled flag via the upsert endpoint (partial PUT carrying only isEnabled). Reloads. */
    suspend fun toggleAlert(eventType: String, enabled: Boolean) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(alertsApi.upsert(channel, eventType, UpdateAlertBody(isEnabled = enabled)))
    }

    /** Delete the response for [eventType]. Reloads on success. Surfaces the error on failure. */
    suspend fun deleteAlert(eventType: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(alertsApi.delete(channel, eventType))
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
        val current: AlertsState = _state.value
        _state.value =
            if (current is AlertsState.Ready) current.copy(actionError = detail)
            else AlertsState.Error(detail)
    }

    private companion object {
        const val NoChannelError: String = "No active channel — reconnect and try again."

        // The dialog only configures chat-message responses (overlay/pipeline are off-page builders); a create
        // always sets this so the backend stores a chat_message rather than its own default.
        const val ChatMessageResponseType: String = "chat_message"
    }
}

/** The Alerts page render state. */
sealed interface AlertsState {
    data object Loading : AlertsState

    /**
     * The channel's event responses are listed. [actionError] is non-null only when the last create/edit/
     * toggle/delete (or a detail fetch) failed — the screen surfaces it as a transient banner while keeping
     * the list rendered.
     */
    data class Ready(val alerts: List<AlertSummary>, val actionError: String? = null) : AlertsState

    data object Empty : AlertsState

    data class Error(val detail: String) : AlertsState
}

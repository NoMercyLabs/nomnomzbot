// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.timers.state

import bot.nomnomz.dashboard.core.realtime.HubEvent
import bot.nomnomz.dashboard.core.realtime.onConfigChange
import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CreateTimerRequest
import bot.nomnomz.dashboard.core.network.EMPTY_PIPELINE_ID
import bot.nomnomz.dashboard.core.network.PickList
import bot.nomnomz.dashboard.core.network.PickListsApi
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.PipelinesApi
import bot.nomnomz.dashboard.core.network.ResourceUsage
import bot.nomnomz.dashboard.core.network.TimerDetail
import bot.nomnomz.dashboard.core.network.TimerSummary
import bot.nomnomz.dashboard.core.network.TimersApi
import bot.nomnomz.dashboard.core.network.UpdateTimerRequest
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_timer_deleted
import nomnomzbot.composeapp.generated.resources.feedback_timer_save_failed
import nomnomzbot.composeapp.generated.resources.feedback_timer_saved
import org.jetbrains.compose.resources.StringResource

// The Timers page's state-holder: resolve the active channel, then load its real scheduled timers from the
// backend (no fabricated rows), and drive the full create / edit / toggle / delete management surface. The
// screen renders [state] for the list and [writeError] for a failed mutation; every successful write reloads
// the list so the screen always reflects the backend's truth.
// S-BUDGETS b1/b3 — the `CountedResource("timers", …)` key on the backend `Timer` entity; the report from
// `GET .../billing/limits` carries the resource under this exact key.
private const val TimersLimitKey: String = "timers"

class TimersController(
    private val channelsApi: ChannelsApi,
    private val timersApi: TimersApi,
    private val pipelinesApi: PipelinesApi,
    private val pickListsApi: PickListsApi,
    private val feedback: Feedback = NoOpFeedback,
    // The channel's truthful resource-limit report (S-BUDGETS-b1's `GET .../billing/limits`), narrowed to just
    // the read this controller needs — a lambda default rather than the full `BillingApi` so this controller's
    // existing tests never have to fake a dozen unrelated billing methods. Defaults to "nothing reported" so a
    // caller that doesn't wire billing gets an unblocked create affordance, never a false block.
    private val resourceLimits: suspend (channelId: String) -> ApiResult<List<ResourceUsage>> =
        { ApiResult.Ok(emptyList()) },
) {
    private val _state: MutableStateFlow<TimersState> = MutableStateFlow(TimersState.Loading)

    /** The page render state: loading / ready (with the rows) / empty / error. */
    val state: StateFlow<TimersState> = _state.asStateFlow()

    private val _timersUsage: MutableStateFlow<ResourceUsage?> = MutableStateFlow(null)

    /**
     * The channel's real [timers][TimersLimitKey] usage — straight from the same billing-limits report
     * `ResourceLimitsSection` renders (S-BUDGETS-b1/b2), never client-computed. Null until [load] resolves it (or
     * when the endpoint carries no report for this resource); the create dialog's warn-before-refuse affordance
     * treats null exactly like "not near the limit" — it must never block on missing data.
     */
    val timersUsage: StateFlow<ResourceUsage?> = _timersUsage.asStateFlow()

    private val _pipelines: MutableStateFlow<List<PipelineSummary>> = MutableStateFlow(emptyList())

    /** The channel's pipelines — populates the "run this pipeline" picker in the timer dialog (supplementary). */
    val pipelines: StateFlow<List<PipelineSummary>> = _pipelines.asStateFlow()

    private val _pickListNames: MutableStateFlow<List<String>> = MutableStateFlow(emptyList())

    /** The channel's random-response list names — feeds the `{list.pick.<name>}` insert helper (supplementary). */
    val pickListNames: StateFlow<List<String>> = _pickListNames.asStateFlow()

    private val _writeError: MutableStateFlow<String?> = MutableStateFlow(null)

    /** The message from the last failed create / edit / toggle / delete, or null when there is none. */
    val writeError: StateFlow<String?> = _writeError.asStateFlow()

    /** Resolve the active channel, then load its scheduled timers. */
    /**
     * Keeps this page live: the backend announces every change to timers on the dashboard hub, from any
     * operator or from the bot itself, and this refetches instead of leaving whatever was on screen when
     * the page opened. Without it the only way to see a change was a manual reload.
     */
    suspend fun subscribeToHub(hubEvents: SharedFlow<HubEvent>) {
        hubEvents.onConfigChange("timers") { load() }
    }

    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch after a mutation keeps
        // the current content on screen (no flash) and swaps it when the new data arrives.
        if (_state.value !is TimersState.Ready) _state.value = TimersState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = TimersState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        // Pipelines are supplementary (they only feed the dialog's picker) — a failure just leaves the picker
        // empty, never fails the page.
        _pipelines.value =
            when (val result: ApiResult<List<PipelineSummary>> = pipelinesApi.list(channel.id)) {
                is ApiResult.Ok -> result.value
                is ApiResult.Failure -> emptyList()
            }

        // Random-response list names for the insert helper — supplementary, so a failure just empties the picker.
        _pickListNames.value =
            when (val result: ApiResult<List<PickList>> = pickListsApi.list()) {
                is ApiResult.Ok -> result.value.map { it.name }
                is ApiResult.Failure -> emptyList()
            }

        // Best-effort: a failure to fetch the limits report just leaves the create affordance unblocked (never
        // fails the page) — the backend still enforces the real limit on the write itself either way.
        _timersUsage.value =
            when (val result: ApiResult<List<ResourceUsage>> = resourceLimits(channel.id)) {
                is ApiResult.Ok -> result.value.firstOrNull { it.limitKey == TimersLimitKey }
                is ApiResult.Failure -> null
            }

        when (val result: ApiResult<List<TimerSummary>> = timersApi.list(channel.id)) {
            is ApiResult.Failure -> _state.value = TimersState.Error(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    if (result.value.isEmpty()) TimersState.Empty
                    else TimersState.Ready(result.value)
        }
    }

    /** Fetch a timer's full detail (pipeline + full message list) to pre-fill the edit dialog. Null on failure. */
    suspend fun timerDetail(id: String): TimerDetail? {
        val channelId: String = resolveChannelId() ?: return null
        return when (val result: ApiResult<TimerDetail> = timersApi.detail(channelId, id)) {
            is ApiResult.Ok -> result.value
            is ApiResult.Failure -> null
        }
    }

    /** Dismiss the current write-error banner (e.g. after the user reads it). */
    fun clearWriteError() {
        _writeError.value = null
    }

    /**
     * Create a new timer from the dialog's fields, then reload the list on success. [messages] is the rotation
     * list (each fires in turn); [pipelineId] optionally binds a pipeline the timer runs each interval.
     */
    suspend fun createTimer(
        name: String,
        messages: List<String>,
        intervalMinutes: Int,
        minChatActivity: Int,
        enabled: Boolean,
        fireOnce: Boolean,
        pipelineId: String?,
    ) {
        val channelId: String = resolveChannelId() ?: return
        val request =
            CreateTimerRequest(
                name = name,
                messages = messages,
                intervalMinutes = intervalMinutes,
                minChatActivity = minChatActivity,
                isEnabled = enabled,
                fireOnce = fireOnce,
                pipelineId = pipelineId,
            )
        runWrite { timersApi.create(channelId, request) }
    }

    /** Update an existing timer with the dialog's fields, then reload the list on success. */
    suspend fun updateTimer(
        id: String,
        name: String,
        messages: List<String>,
        intervalMinutes: Int,
        minChatActivity: Int,
        enabled: Boolean,
        fireOnce: Boolean,
        pipelineId: String?,
    ) {
        val channelId: String = resolveChannelId() ?: return
        val request =
            UpdateTimerRequest(
                name = name,
                messages = messages,
                intervalMinutes = intervalMinutes,
                minChatActivity = minChatActivity,
                isEnabled = enabled,
                fireOnce = fireOnce,
                // A null pipelineId is dropped by the serializer (explicitNulls=false), so "None" on a bound
                // timer needs the empty sentinel to actually unbind — the backend maps it to null.
                pipelineId = pipelineId ?: EMPTY_PIPELINE_ID,
            )
        runWrite { timersApi.update(channelId, id, request) }
    }

    /** Delete a timer, then reload the list on success. */
    suspend fun deleteTimer(id: String) {
        val channelId: String = resolveChannelId() ?: return
        runWrite(success = Res.string.feedback_timer_deleted) { timersApi.delete(channelId, id) }
    }

    /**
     * Flip a timer's enabled state. The backend toggle endpoint flips the stored value server-side, so the
     * [enabled] the row clicked from is informational; the reload reflects the new truth either way.
     */
    suspend fun toggleTimer(id: String, enabled: Boolean) {
        val channelId: String = resolveChannelId() ?: return
        runWrite { timersApi.toggle(channelId, id) }
    }

    // Resolve the active channel for a write; surface a write error (not a full-page error) if none resolves,
    // so the list the user is looking at stays put.
    private suspend fun resolveChannelId(): String? =
        when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
            is ApiResult.Failure -> {
                _writeError.value = result.error.message
                null
            }
            is ApiResult.Ok -> result.value.id
        }

    // Run a mutation: on success clear any prior error, announce it on the frame, and reload the list; on
    // failure surface the message (both the in-page banner AND the frame-level error) and leave the current
    // list untouched. [success] lets a delete say "Deleted" while the rest default to "Saved".
    private suspend fun runWrite(
        success: StringResource = Res.string.feedback_timer_saved,
        write: suspend () -> ApiResult<Unit>,
    ) {
        when (val result: ApiResult<Unit> = write()) {
            is ApiResult.Failure -> {
                _writeError.value = result.error.message
                feedback.error(Res.string.feedback_timer_save_failed, result.error.message)
            }
            is ApiResult.Ok -> {
                _writeError.value = null
                feedback.success(success)
                load()
            }
        }
    }
}

/** The Timers page render state. */
sealed interface TimersState {
    data object Loading : TimersState

    data class Ready(val timers: List<TimerSummary>) : TimersState

    data object Empty : TimersState

    data class Error(val detail: String) : TimersState
}

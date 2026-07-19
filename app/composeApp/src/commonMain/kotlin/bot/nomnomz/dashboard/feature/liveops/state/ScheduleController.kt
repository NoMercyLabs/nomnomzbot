// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.liveops.state

import bot.nomnomz.dashboard.core.designsystem.component.PickerOption
import bot.nomnomz.dashboard.core.io.JournalFileIO
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.Category
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CreateScheduleSegmentBody
import bot.nomnomz.dashboard.core.network.LiveOpsApi
import bot.nomnomz.dashboard.core.network.LiveOpsSchedule
import bot.nomnomz.dashboard.core.network.StreamApi
import bot.nomnomz.dashboard.core.network.UpdateScheduleSegmentBody
import bot.nomnomz.dashboard.core.network.UpdateScheduleSettingsBody
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The stream-schedule page's state-holder: resolves the active channel, reads its real Twitch schedule (segments
// + vacation window), and persists edits back — add / edit / cancel / delete a segment and set the vacation
// window. Each write re-reads the schedule on success so the page always reflects Twitch's truth (no fabricated
// rows). The screen renders [state]; a retry / reconnect calls [load] again. The resolved channel id is cached
// from [load] so writes reuse it without re-resolving.
class ScheduleController(
    private val channelsApi: ChannelsApi,
    private val liveOpsApi: LiveOpsApi,
    private val streamApi: StreamApi,
    private val fileBridge: JournalFileIO,
) {
    private val _state: MutableStateFlow<ScheduleState> = MutableStateFlow(ScheduleState.Loading)

    /** The page render state: loading / ready (the schedule) / empty (nothing scheduled) / error. */
    val state: StateFlow<ScheduleState> = _state.asStateFlow()

    /** The channel resolved by the last successful [load]; writes target it without re-resolving. */
    private var channelId: String? = null

    /** Resolve the active channel, then read its stream schedule. */
    suspend fun load() {
        if (_state.value !is ScheduleState.Ready) _state.value = ScheduleState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = ScheduleState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id

        _state.value =
            when (val result: ApiResult<LiveOpsSchedule> = liveOpsApi.getSchedule(channel.id)) {
                is ApiResult.Failure -> ScheduleState.Error(result.error.message)
                is ApiResult.Ok -> {
                    val schedule: LiveOpsSchedule = result.value
                    if (schedule.segments.isEmpty() && schedule.vacation == null) {
                        ScheduleState.Empty
                    } else {
                        ScheduleState.Ready(schedule = schedule)
                    }
                }
            }
    }

    /**
     * Add a schedule segment starting at [startTime] (ISO-8601) in [timezone] (IANA) for [duration] minutes,
     * optionally [isRecurring] weekly, with a [title] and [categoryId]. Reloads on success; surfaces the error.
     */
    suspend fun addSegment(
        startTime: String,
        timezone: String,
        duration: String,
        isRecurring: Boolean,
        title: String?,
        categoryId: String?,
    ) {
        val channel: String = channelId ?: return
        afterWrite(
            liveOpsApi.createScheduleSegment(
                channel,
                CreateScheduleSegmentBody(
                    startTime = startTime,
                    timezone = timezone,
                    duration = duration,
                    isRecurring = isRecurring,
                    title = title?.ifBlank { null },
                    categoryId = categoryId?.ifBlank { null },
                ),
            )
        )
    }

    /** Edit segment [segmentId] — only the supplied fields change. Reloads on success; surfaces the error. */
    suspend fun editSegment(
        segmentId: String,
        startTime: String?,
        duration: String?,
        timezone: String?,
        title: String?,
        categoryId: String?,
    ) {
        val channel: String = channelId ?: return
        afterWrite(
            liveOpsApi.updateScheduleSegment(
                channel,
                segmentId,
                UpdateScheduleSegmentBody(
                    startTime = startTime?.ifBlank { null },
                    duration = duration?.ifBlank { null },
                    timezone = timezone?.ifBlank { null },
                    title = title?.ifBlank { null },
                    categoryId = categoryId?.ifBlank { null },
                ),
            )
        )
    }

    /** Cancel this occurrence of segment [segmentId] (recurring streams keep the series). Reloads on success. */
    suspend fun cancelSegment(segmentId: String) {
        val channel: String = channelId ?: return
        afterWrite(
            liveOpsApi.updateScheduleSegment(channel, segmentId, UpdateScheduleSegmentBody(isCanceled = true))
        )
    }

    /** Delete segment [segmentId] from the schedule. Reloads on success; surfaces the error on failure. */
    suspend fun deleteSegment(segmentId: String) {
        val channel: String = channelId ?: return
        afterWrite(liveOpsApi.deleteScheduleSegment(channel, segmentId))
    }

    /**
     * Set the vacation window — [enabled] on/off, its [startTime]..[endTime] (ISO-8601) and [timezone]. Reloads
     * on success; surfaces the error on failure. No-ops when no channel is loaded.
     */
    suspend fun setVacation(enabled: Boolean, startTime: String?, endTime: String?, timezone: String?) {
        val channel: String = channelId ?: return
        afterWrite(
            liveOpsApi.updateScheduleSettings(
                channel,
                UpdateScheduleSettingsBody(
                    isVacationEnabled = enabled,
                    vacationStartTime = startTime?.ifBlank { null },
                    vacationEndTime = endTime?.ifBlank { null },
                    timezone = timezone?.ifBlank { null },
                ),
            )
        )
    }

    /**
     * Download the schedule as an `.ics` (iCalendar) file. Fetches the authenticated iCalendar snapshot and
     * hands it to the platform file bridge (a native Save dialog on desktop, a browser download on web).
     * Returns true when the file was written, false when the user cancelled or the fetch failed (the error is
     * surfaced on the Ready state). This is a one-time snapshot, NOT a live webcal subscription — the endpoint
     * is Bearer-authenticated, so a calendar app cannot poll it directly.
     */
    suspend fun downloadIcalendar(): Boolean {
        val channel: String = channelId ?: return false
        return when (val result: ApiResult<String> = liveOpsApi.getScheduleIcalendar(channel)) {
            is ApiResult.Failure -> {
                val current: ScheduleState = _state.value
                if (current is ScheduleState.Ready) {
                    _state.value = current.copy(actionError = result.error.message)
                }
                false
            }
            is ApiResult.Ok -> fileBridge.saveFile("schedule.ics", result.value.encodeToByteArray())
        }
    }

    /**
     * Autocomplete Twitch categories for the segment editor's category picker. Maps each match to a
     * [PickerOption] whose [PickerOption.id] is the Twitch category id the segment write consumes and
     * [PickerOption.label] the canonical game name. Best-effort: empty on failure or before the channel resolves.
     */
    suspend fun searchCategories(query: String): List<PickerOption> {
        val channel: String = channelId ?: return emptyList()
        return when (val result: ApiResult<List<Category>> = streamApi.searchCategories(channel, query)) {
            is ApiResult.Ok -> result.value.map { PickerOption(id = it.id, label = it.name) }
            is ApiResult.Failure -> emptyList()
        }
    }

    // Reload on success; on failure surface the message on the current Ready state without losing the schedule.
    private suspend fun afterWrite(result: ApiResult<Unit>) {
        when (result) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> {
                val current: ScheduleState = _state.value
                _state.value =
                    if (current is ScheduleState.Ready) current.copy(actionError = result.error.message)
                    else ScheduleState.Error(result.error.message)
            }
        }
    }
}

/** The stream-schedule page render state. */
sealed interface ScheduleState {
    data object Loading : ScheduleState

    /** The loaded schedule (segments + vacation), plus an optional message when the last write failed. */
    data class Ready(val schedule: LiveOpsSchedule, val actionError: String? = null) : ScheduleState

    /** Nothing scheduled yet and no vacation window — the screen shows the add-segment affordance. */
    data object Empty : ScheduleState

    data class Error(val detail: String) : ScheduleState
}

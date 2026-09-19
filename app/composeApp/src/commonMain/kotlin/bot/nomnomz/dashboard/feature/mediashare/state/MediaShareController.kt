// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.mediashare.state

import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.MediaShareApi
import bot.nomnomz.dashboard.core.network.MediaShareConfig
import bot.nomnomz.dashboard.core.network.MediaShareRequest
import bot.nomnomz.dashboard.core.network.UpdateMediaShareConfigBody
import bot.nomnomz.dashboard.core.realtime.HubEvent
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.mediashare_action_error

// The Media-Share moderator-queue page state-holder (media-share.md §5): the channel's clip queue (approve /
// reject / skip / mark-played / reorder) and the channel's Media-Share config. The active channel rides in the
// X-Channel-Id header, so this holder never threads a channelId — it reads the queue and config straight off the
// facade and rebuilds a projection the screen renders.
//
// The queue is fatal on read failure (there is nothing to moderate without it); the config is best-effort — a
// failure falls back to a default [MediaShareConfig] so the queue still renders. A failed write surfaces a
// transient [MediaShareUiState.Ready.actionError] banner rather than tearing down the page.
class MediaShareController(
    private val mediaShareApi: MediaShareApi,
    private val feedback: Feedback = NoOpFeedback,
) {
    private val _state: MutableStateFlow<MediaShareUiState> = MutableStateFlow(MediaShareUiState.Loading)

    /** The page render state: loading / ready (queue + config) / error. */
    val state: StateFlow<MediaShareUiState> = _state.asStateFlow()

    // The active lane filter. Kept so a write, or a hub-pushed reload, re-reads the same lane the mod is viewing.
    private var lane: MediaShareLane = MediaShareLane.Active

    /** Read the whole queue (fatal on failure) + the config (best-effort → a default on failure). */
    suspend fun load() {
        if (_state.value !is MediaShareUiState.Ready) _state.value = MediaShareUiState.Loading
        lane = MediaShareLane.Active

        val queue: List<MediaShareRequest> =
            when (val result: ApiResult<List<MediaShareRequest>> = mediaShareApi.queue(lane.serverStatus)) {
                is ApiResult.Failure -> {
                    _state.value = MediaShareUiState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> lane.applyTo(result.value)
            }

        // The config is a convenience panel — a failure just falls back to defaults so the queue still renders.
        val config: MediaShareConfig =
            when (val result: ApiResult<MediaShareConfig> = mediaShareApi.config()) {
                is ApiResult.Ok -> result.value
                is ApiResult.Failure -> MediaShareConfig()
            }

        _state.value = MediaShareUiState.Ready(queue = queue, config = config, lane = lane)
    }

    /** Re-read the queue in the [newLane], keeping the current config. */
    suspend fun setStatusFilter(newLane: MediaShareLane) {
        val current: MediaShareUiState.Ready = _state.value as? MediaShareUiState.Ready ?: return
        lane = newLane

        when (val result: ApiResult<List<MediaShareRequest>> = mediaShareApi.queue(newLane.serverStatus)) {
            is ApiResult.Ok -> _state.value = current.copy(queue = newLane.applyTo(result.value), lane = newLane)
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    /**
     * Subscribe to [hubEvents] so the queue refreshes when another mod (or the overlay/player) changes a clip's
     * playback state — a `media_share_playback_changed` [HubEvent.ChannelEvent] pushed by the backend whenever
     * approve/skip/played/reject/reorder moves an item, so a played clip drops out of THIS session's Active lane
     * without waiting for a manual reload (S-OBS-09b).
     */
    suspend fun subscribeToHub(hubEvents: SharedFlow<HubEvent>) {
        hubEvents.collect { evt ->
            if (evt is HubEvent.ChannelEvent && evt.event.type == "media_share_playback_changed") {
                refreshQueue()
            }
        }
    }

    /** Approve a pending clip, then re-read the queue. */
    suspend fun approve(id: String) = afterWrite(mediaShareApi.approve(id))

    /** Reject a clip, then re-read the queue. */
    suspend fun reject(id: String) = afterWrite(mediaShareApi.reject(id))

    /** Skip a clip, then re-read the queue. */
    suspend fun skip(id: String) = afterWrite(mediaShareApi.skip(id))

    /** Mark a clip played, then re-read the queue. */
    suspend fun markPlayed(id: String) = afterWrite(mediaShareApi.played(id))

    /** Move a clip to [position] in the queue (0-based), then re-read. */
    suspend fun reorder(id: String, position: Int) = afterWrite(mediaShareApi.reorder(id, position))

    /** Persist the edited config. On success the Ready state adopts the returned config; else a banner. */
    suspend fun saveConfig(config: MediaShareConfig) {
        val body: UpdateMediaShareConfigBody =
            UpdateMediaShareConfigBody(
                isEnabled = config.isEnabled,
                requireApproval = config.requireApproval,
                allowTwitchClips = config.allowTwitchClips,
                allowYouTube = config.allowYouTube,
                maxDurationSeconds = config.maxDurationSeconds,
                entryCost = config.entryCost,
                maxQueueLength = config.maxQueueLength,
                perUserCooldownSeconds = config.perUserCooldownSeconds,
            )
        when (val result: ApiResult<MediaShareConfig> = mediaShareApi.updateConfig(body)) {
            is ApiResult.Ok -> {
                val current: MediaShareUiState.Ready = _state.value as? MediaShareUiState.Ready ?: return
                _state.value = current.copy(config = result.value)
            }
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    // ── internals ────────────────────────────────────────────────────────────

    // A queue write echoes the updated request, but the page re-lists the lane after every write, so the body is
    // ignored here — success re-reads, failure raises the transient banner.
    private suspend fun afterWrite(result: ApiResult<MediaShareRequest>) {
        when (result) {
            is ApiResult.Ok -> refreshQueue()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    private suspend fun refreshQueue() {
        val previous: MediaShareUiState.Ready = _state.value as? MediaShareUiState.Ready ?: return
        when (val result: ApiResult<List<MediaShareRequest>> = mediaShareApi.queue(lane.serverStatus)) {
            is ApiResult.Ok -> _state.value = previous.copy(queue = lane.applyTo(result.value))
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    private fun failWrite(detail: String) {
        val current: MediaShareUiState = _state.value
        if (current is MediaShareUiState.Ready) feedback.error(Res.string.mediashare_action_error, detail)
        else _state.value = MediaShareUiState.Error(detail)
    }
}

/**
 * The queue lane a mod is viewing. [Active] is the default (S-OBS-09b) — pending/approved/playing only, so a
 * played or rejected clip drops out of view the instant it leaves that set, instead of lingering forever in an
 * unfiltered list. [All] is the one lane that still shows every status (incl. rejected/skipped) as history;
 * [Pending]/[Approved]/[Played] mirror one backend status each.
 */
enum class MediaShareLane {
    Active,
    All,
    Pending,
    Approved,
    Played,
    ;

    /** The `?status=` query value the backend understands — null for [Active]/[All] (both fetch everything, then
     * [Active] filters client-side; the backend has no combined "not played/rejected" filter to ask for). */
    val serverStatus: String?
        get() = when (this) {
            Pending -> "pending"
            Approved -> "approved"
            Played -> "played"
            Active, All -> null
        }

    /** Applies this lane's client-side narrowing to a freshly-fetched queue. Only [Active] narrows; every other
     * lane already got exactly its rows from the backend (or, for [All], wants every row unfiltered). */
    fun applyTo(queue: List<MediaShareRequest>): List<MediaShareRequest> =
        if (this == Active) queue.filter { it.status == "pending" || it.status == "approved" || it.status == "playing" }
        else queue
}

/** The Media-Share page render state. */
sealed interface MediaShareUiState {
    data object Loading : MediaShareUiState

    /**
     * The channel's clip [queue] in the active [lane] and its [config]. A failed write announces on the
     * shell-level feedback toast rather than a field here — see [MediaShareController.failWrite].
     */
    data class Ready(
        val queue: List<MediaShareRequest>,
        val config: MediaShareConfig,
        val lane: MediaShareLane = MediaShareLane.Active,
    ) : MediaShareUiState

    data class Error(val detail: String) : MediaShareUiState
}

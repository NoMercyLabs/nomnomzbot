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

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.MediaShareApi
import bot.nomnomz.dashboard.core.network.MediaShareConfig
import bot.nomnomz.dashboard.core.network.MediaShareRequest
import bot.nomnomz.dashboard.core.network.UpdateMediaShareConfigBody
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Media-Share moderator-queue page state-holder (media-share.md §5): the channel's clip queue (approve /
// reject / skip / mark-played / reorder) and the channel's Media-Share config. The active channel rides in the
// X-Channel-Id header, so this holder never threads a channelId — it reads the queue and config straight off the
// facade and rebuilds a projection the screen renders.
//
// The queue is fatal on read failure (there is nothing to moderate without it); the config is best-effort — a
// failure falls back to a default [MediaShareConfig] so the queue still renders. A failed write surfaces a
// transient [MediaShareUiState.Ready.actionError] banner rather than tearing down the page.
class MediaShareController(private val mediaShareApi: MediaShareApi) {
    private val _state: MutableStateFlow<MediaShareUiState> = MutableStateFlow(MediaShareUiState.Loading)

    /** The page render state: loading / ready (queue + config) / error. */
    val state: StateFlow<MediaShareUiState> = _state.asStateFlow()

    // The active lane filter (null = whole queue). Kept so a write re-reads the same lane the mod is viewing.
    private var statusFilter: String? = null

    /** Read the whole queue (fatal on failure) + the config (best-effort → a default on failure). */
    suspend fun load() {
        if (_state.value !is MediaShareUiState.Ready) _state.value = MediaShareUiState.Loading
        statusFilter = null

        val queue: List<MediaShareRequest> =
            when (val result: ApiResult<List<MediaShareRequest>> = mediaShareApi.queue(null)) {
                is ApiResult.Failure -> {
                    _state.value = MediaShareUiState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        // The config is a convenience panel — a failure just falls back to defaults so the queue still renders.
        val config: MediaShareConfig =
            when (val result: ApiResult<MediaShareConfig> = mediaShareApi.config()) {
                is ApiResult.Ok -> result.value
                is ApiResult.Failure -> MediaShareConfig()
            }

        _state.value = MediaShareUiState.Ready(queue = queue, config = config, statusFilter = null)
    }

    /** Re-read the queue in the [status] lane (null = all), keeping the current config. */
    suspend fun setStatusFilter(status: String?) {
        val current: MediaShareUiState.Ready = _state.value as? MediaShareUiState.Ready ?: return
        statusFilter = status

        when (val result: ApiResult<List<MediaShareRequest>> = mediaShareApi.queue(status)) {
            is ApiResult.Ok ->
                _state.value = current.copy(queue = result.value, statusFilter = status, actionError = null)
            is ApiResult.Failure -> failWrite(result.error.message)
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
                _state.value = current.copy(config = result.value, actionError = null)
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
        when (val result: ApiResult<List<MediaShareRequest>> = mediaShareApi.queue(statusFilter)) {
            is ApiResult.Ok -> _state.value = previous.copy(queue = result.value, actionError = null)
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    private fun failWrite(detail: String) {
        val current: MediaShareUiState = _state.value
        _state.value =
            if (current is MediaShareUiState.Ready) current.copy(actionError = detail)
            else MediaShareUiState.Error(detail)
    }
}

/** The Media-Share page render state. */
sealed interface MediaShareUiState {
    data object Loading : MediaShareUiState

    /**
     * The channel's clip [queue] in the active [statusFilter] lane (null = all) and its [config]. [actionError]
     * is non-null only when the last write failed — a transient banner over the content.
     */
    data class Ready(
        val queue: List<MediaShareRequest>,
        val config: MediaShareConfig,
        val statusFilter: String? = null,
        val actionError: String? = null,
    ) : MediaShareUiState

    data class Error(val detail: String) : MediaShareUiState
}

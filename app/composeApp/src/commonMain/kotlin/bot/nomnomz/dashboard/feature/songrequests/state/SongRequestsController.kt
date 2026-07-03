// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.songrequests.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.MusicConfig
import bot.nomnomz.dashboard.core.network.QueuedSong
import bot.nomnomz.dashboard.core.network.SongRequestsApi
import bot.nomnomz.dashboard.core.network.UpdateMusicConfigBody
import bot.nomnomz.dashboard.core.realtime.HubEvent
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Song Requests page's state-holder — the channel's live queue AND its SR management capabilities:
// config (max queue, allowed providers, trust floor) and the public SR-page token. Loads all three in
// parallel on [load]; controls affect the queue only and reload on success. No fabricated tracks.
class SongRequestsController(
    private val channelsApi: ChannelsApi,
    private val songRequestsApi: SongRequestsApi,
) {
    private val _state: MutableStateFlow<SongRequestsState> =
        MutableStateFlow(SongRequestsState.Loading)

    /** The page render state. */
    val state: StateFlow<SongRequestsState> = _state.asStateFlow()

    // Resolved channel id — set on first load, reused by control actions so they target the same channel.
    private var channelId: String? = null

    // Last-seen track id: used by subscribeToHub to skip reload when only play/pause state changed.
    private var lastTrackId: String? = null

    /** Resolve the active channel, then load its queue, config, and SR-page token in parallel. */
    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch after a mutation keeps
        // the current content on screen (no flash) and swaps it when the new data arrives.
        if (_state.value !is SongRequestsState.Ready) _state.value = SongRequestsState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = SongRequestsState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        channelId = channel.id

        coroutineScope {
            val queueDeferred = async { songRequestsApi.queue(channel.id) }
            val configDeferred = async { songRequestsApi.config(channel.id) }
            val tokenDeferred = async { songRequestsApi.srPageToken(channel.id) }

            val queueResult: ApiResult<List<QueuedSong>> = queueDeferred.await()
            val configResult: ApiResult<MusicConfig> = configDeferred.await()
            val tokenResult: ApiResult<String> = tokenDeferred.await()

            if (queueResult is ApiResult.Failure) {
                _state.value = SongRequestsState.Error(queueResult.error.message)
                return@coroutineScope
            }

            val queue: List<QueuedSong> = (queueResult as ApiResult.Ok).value
            val config: MusicConfig? = (configResult as? ApiResult.Ok)?.value
            val srPageToken: String? = (tokenResult as? ApiResult.Ok)?.value

            _state.value = SongRequestsState.Ready(
                queue = queue,
                config = config,
                srPageToken = srPageToken,
            )
        }
    }

    /** Skip the current track. Reloads on success. */
    suspend fun skip() = control { channel -> songRequestsApi.skip(channel) }

    /** Pause playback. Reloads on success. */
    suspend fun pause() = control { channel -> songRequestsApi.pause(channel) }

    /** Resume playback. Reloads on success. */
    suspend fun resume() = control { channel -> songRequestsApi.resume(channel) }

    /**
     * Remove the queued song at [position]. Reloads on success; surfaces the error on failure.
     * The screen gates this behind a confirmation before calling.
     */
    suspend fun remove(position: Int) = control { channel -> songRequestsApi.remove(channel, position) }

    /** Save a patched SR / music config. Reloads on success. */
    suspend fun updateConfig(body: UpdateMusicConfigBody) {
        val channel: String = channelId ?: return
        when (val result: ApiResult<MusicConfig> = songRequestsApi.updateConfig(channel, body)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> surfaceError(result.error.message)
        }
    }

    /** Rotate the SR-page token so the old share link stops working. */
    suspend fun rotateSrPageToken() {
        val channel: String = channelId ?: return
        when (val result: ApiResult<String> = songRequestsApi.rotateSrPageToken(channel)) {
            is ApiResult.Ok -> {
                val current: SongRequestsState = _state.value
                if (current is SongRequestsState.Ready)
                    _state.value = current.copy(srPageToken = result.value)
            }
            is ApiResult.Failure -> surfaceError(result.error.message)
        }
    }

    /**
     * Subscribe to [hubEvents] so the queue refreshes when the current track changes. A play/pause toggle
     * does not advance the queue so it is skipped — only a new (or cleared) track triggers a reload.
     */
    suspend fun subscribeToHub(hubEvents: SharedFlow<HubEvent>) {
        hubEvents.collect { evt ->
            if (evt !is HubEvent.MusicStateChanged) return@collect
            val incomingId: String? = evt.state.currentTrack?.trackName
            if (incomingId == lastTrackId) return@collect
            lastTrackId = incomingId
            if (channelId != null) load()
        }
    }

    // Shared control flow: run [action]; reload on success, surface the error on the Ready state on failure.
    private suspend fun control(action: suspend (channel: String) -> ApiResult<Unit>) {
        val channel: String = channelId ?: return
        when (val result: ApiResult<Unit> = action(channel)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> surfaceError(result.error.message)
        }
    }

    private fun surfaceError(message: String) {
        val current: SongRequestsState = _state.value
        if (current is SongRequestsState.Ready)
            _state.value = current.copy(actionError = message)
    }
}

/** The Song Requests page render state. */
sealed interface SongRequestsState {
    data object Loading : SongRequestsState

    /**
     * Loaded: the live queue, the SR config, and the SR-page token. [config] and [srPageToken] may be null
     * when the backend call failed (resilient — the queue still renders). [actionError] surfaces the last
     * control-action failure while keeping the queue intact.
     */
    data class Ready(
        val queue: List<QueuedSong>,
        val config: MusicConfig?,
        val srPageToken: String?,
        val actionError: String? = null,
    ) : SongRequestsState

    data class Error(val detail: String) : SongRequestsState
}

// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.music.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.MusicApi
import bot.nomnomz.dashboard.core.network.MusicConfig
import bot.nomnomz.dashboard.core.network.MusicSnapshot
import bot.nomnomz.dashboard.core.network.MusicTrack
import bot.nomnomz.dashboard.core.network.NowPlaying
import bot.nomnomz.dashboard.core.network.MusicDevice
import bot.nomnomz.dashboard.core.network.MusicPlaylist
import bot.nomnomz.dashboard.core.network.MusicSongRequestBody
import bot.nomnomz.dashboard.core.network.UpdateMusicConfigBody
import bot.nomnomz.dashboard.core.realtime.HubEvent
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Music page's state-holder — the channel's live playback, made controllable. Resolves the active channel,
// loads its real now-playing + queue from the backend (the connected music provider; no fabricated tracks),
// and drives the supported playback controls: play (resume) / pause / skip, plus removing a queued song by
// position. The screen renders [state]; a retry / reconnect calls [load] again. A control hits the backend and
// reloads on success so the now-playing and queue both re-project; on failure the snapshot stays put and the
// error surfaces on the Ready state. Play vs pause is one control driven by the now-playing isPlaying flag.
class MusicController(
    private val channelsApi: ChannelsApi,
    private val musicApi: MusicApi,
    // The active backend origin, read live so the pretty share link (`{origin}/sr/@name`) matches whatever host
    // served the dashboard. Null (the default, e.g. in tests) simply omits the absolute link.
    private val baseUrlProvider: () -> String? = { null },
) {
    private val _state: MutableStateFlow<MusicState> = MutableStateFlow(MusicState.Loading)

    /** The page render state: loading / ready (now-playing + queue) / empty / error. */
    val state: StateFlow<MusicState> = _state.asStateFlow()

    // The channel the loaded snapshot belongs to, kept so controls target the same channel without re-resolving.
    private var channelId: String? = null

    /** Resolve the active channel, then load its now-playing track and upcoming queue. */
    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch after a mutation keeps
        // the current content on screen (no flash) and swaps it when the new data arrives.
        if (_state.value !is MusicState.Ready) _state.value = MusicState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = MusicState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        channelId = channel.id

        val snapshot: MusicSnapshot =
            when (val result: ApiResult<MusicSnapshot> = musicApi.queue(channel.id)) {
                is ApiResult.Failure -> {
                    _state.value = MusicState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        // Config is resilient — a failure degrades to null; the playback controls still render.
        val config: MusicConfig? =
            when (val result: ApiResult<MusicConfig> = musicApi.config(channel.id)) {
                is ApiResult.Failure -> null
                is ApiResult.Ok -> result.value
            }

        // SR-page token is also resilient — null if the backend doesn't have one minted yet or returns error.
        val srToken: String? =
            when (val result: ApiResult<String> = musicApi.srPageToken(channel.id)) {
                is ApiResult.Failure -> null
                is ApiResult.Ok -> result.value
            }

        // Devices and playlists are resilient — failures degrade to empty lists.
        val devices: List<MusicDevice> =
            when (val result: ApiResult<List<MusicDevice>> = musicApi.getDevices(channel.id)) {
                is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }

        val playlists: List<MusicPlaylist> =
            when (val result: ApiResult<List<MusicPlaylist>> = musicApi.getPlaylists(channel.id)) {
                is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }

        // The pretty, human-shareable SR link — `{origin}/sr/@{login}` — resolvable by the public by-channel
        // route (@ + case tolerant). Only built when both the origin and the channel login are known.
        val shareLink: String? =
            baseUrlProvider()?.trimEnd('/')?.takeIf { it.isNotBlank() }?.let { origin ->
                channel.login.takeIf { it.isNotBlank() }?.let { login -> "$origin/sr/@$login" }
            }

        _state.value =
            if (snapshot.nowPlaying == null && snapshot.queue.isEmpty() && config == null) MusicState.Empty
            else MusicState.Ready(
                nowPlaying = snapshot.nowPlaying,
                queue = snapshot.queue,
                config = config,
                srPageToken = srToken,
                shareLink = shareLink,
                devices = devices,
                playlists = playlists,
            )
    }

    /**
     * Add a song to the queue by search [query], attributed to [requestedBy]. Reloads on success so the new
     * entry appears; surfaces the error without clearing the current queue on failure.
     */
    suspend fun addToQueue(query: String, requestedBy: String) {
        val channel: String = channelId ?: return
        control { musicApi.addToQueue(channel, MusicSongRequestBody(query, requestedBy)) }
    }

    /**
     * Persist a partial config update. Only the fields present in [body] are changed; everything else is
     * carried unchanged by the backend. Reloads on success (the new config replaces the old); surfaces the
     * error on the Ready state on failure.
     */
    suspend fun updateConfig(body: UpdateMusicConfigBody) {
        val channel: String = channelId ?: return
        when (val result: ApiResult<MusicConfig> = musicApi.updateConfig(channel, body)) {
            is ApiResult.Failure -> {
                val current: MusicState = _state.value
                if (current is MusicState.Ready) {
                    _state.value = current.copy(actionError = result.error.message)
                }
            }
            is ApiResult.Ok -> load()
        }
    }

    /** Rotate the SR-page token. The new token replaces the old on the Ready state. */
    suspend fun rotateSrPageToken() {
        val channel: String = channelId ?: return
        when (val result: ApiResult<String> = musicApi.rotateSrPageToken(channel)) {
            is ApiResult.Failure -> {
                val current: MusicState = _state.value
                if (current is MusicState.Ready) {
                    _state.value = current.copy(actionError = result.error.message)
                }
            }
            is ApiResult.Ok -> {
                val current: MusicState = _state.value
                if (current is MusicState.Ready) {
                    _state.value = current.copy(srPageToken = result.value)
                }
            }
        }
    }

    /** Seek to [positionMs] in the current track. */
    suspend fun seek(positionMs: Int) = control { channel -> musicApi.seek(channel, positionMs) }

    /** Enable or disable shuffle. */
    suspend fun setShuffle(enabled: Boolean) = control { channel -> musicApi.setShuffle(channel, enabled) }

    /** Set repeat mode: "off", "track", or "context". */
    suspend fun setRepeat(mode: String) = control { channel -> musicApi.setRepeat(channel, mode) }

    /** Transfer playback to another device. */
    suspend fun transferPlayback(deviceId: String, play: Boolean = false) =
        control { channel -> musicApi.transferPlayback(channel, deviceId, play) }

    /** Start playback of a playlist or album context URI. */
    suspend fun playContext(contextUri: String) = control { channel -> musicApi.playContext(channel, contextUri) }

    /**
     * Subscribe to [hubEvents] so the now-playing state updates in real-time without a poll:
     * - [HubEvent.MusicStateChanged]: updates [MusicState.Ready.nowPlaying] from the hub payload; a null track
     *   means nothing is playing.
     */
    suspend fun subscribeToHub(hubEvents: SharedFlow<HubEvent>) {
        hubEvents.collect { evt ->
            if (evt !is HubEvent.MusicStateChanged) return@collect
            val current: MusicState = _state.value
            if (current !is MusicState.Ready) return@collect
            val track: NowPlaying? =
                if (evt.state.currentTrack == null) null
                else NowPlaying(
                    trackName = evt.state.currentTrack.trackName,
                    artist = evt.state.currentTrack.artist,
                    album = evt.state.currentTrack.album,
                    imageUrl = evt.state.currentTrack.albumArtUrl,
                    durationMs = evt.state.currentTrack.durationMs,
                    isPlaying = evt.state.isPlaying,
                    provider = evt.state.currentTrack.provider,
                )
            _state.value = current.copy(nowPlaying = track)
        }
    }

    /** Clear the last action error. */
    fun clearError() {
        val current: MusicState = _state.value
        if (current is MusicState.Ready) _state.value = current.copy(actionError = null)
    }

    /** Pause playback. Reloads the snapshot on success; surfaces the error on the Ready state on failure. */
    suspend fun pause() = control { channel -> musicApi.pause(channel) }

    /** Resume (start) playback. Reloads the snapshot on success; surfaces the error on the Ready state. */
    suspend fun resume() = control { channel -> musicApi.resume(channel) }

    /** Skip the current track. Reloads the snapshot on success; surfaces the error on the Ready state. */
    suspend fun skip() = control { channel -> musicApi.skip(channel) }

    /**
     * Remove the queued song at [position] (a [MusicTrack.position]). On success the snapshot reloads so the
     * removed song drops off; on failure the current snapshot stays put and the error surfaces on the Ready
     * state. The screen gates this destructive action behind a confirmation, so it only runs on a confirmed
     * click.
     */
    suspend fun remove(position: Int) = control { channel -> musicApi.remove(channel, position) }

    // The shared control flow: run [action] against the resolved channel; reload the snapshot on success, or
    // keep the current one and surface the failure on the Ready state. No channel resolved yet → no-op.
    private suspend fun control(action: suspend (channel: String) -> ApiResult<Unit>) {
        val channel: String = channelId ?: return

        when (val result: ApiResult<Unit> = action(channel)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> {
                val current: MusicState = _state.value
                if (current is MusicState.Ready) {
                    _state.value = current.copy(actionError = result.error.message)
                }
            }
        }
    }
}

/** The Music page render state. */
sealed interface MusicState {
    data object Loading : MusicState

    /**
     * The live playback snapshot: the [nowPlaying] track (null when nothing is playing), the upcoming [queue],
     * and an optional [actionError] when the last control failed (the snapshot is intact). The screen drives
     * play/pause from [NowPlaying.isPlaying] and offers a per-track remove on the queue. [config] is null
     * when the config endpoint is unavailable (the playback section still renders). [srPageToken] is the
     * minted SR-page shareable token (null if not yet minted or the endpoint is down).
     */
    data class Ready(
        val nowPlaying: NowPlaying?,
        val queue: List<MusicTrack>,
        val config: MusicConfig? = null,
        val srPageToken: String? = null,
        // The absolute, human-friendly public SR link (`{origin}/sr/@name`); null when the origin/login is unknown.
        val shareLink: String? = null,
        val devices: List<MusicDevice> = emptyList(),
        val playlists: List<MusicPlaylist> = emptyList(),
        val actionError: String? = null,
    ) : MusicState

    data object Empty : MusicState

    data class Error(val detail: String) : MusicState
}

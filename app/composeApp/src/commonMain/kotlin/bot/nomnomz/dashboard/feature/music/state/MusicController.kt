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
import bot.nomnomz.dashboard.core.network.MusicSnapshot
import bot.nomnomz.dashboard.core.network.MusicTrack
import bot.nomnomz.dashboard.core.network.NowPlaying
import kotlinx.coroutines.flow.MutableStateFlow
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
) {
    private val _state: MutableStateFlow<MusicState> = MutableStateFlow(MusicState.Loading)

    /** The page render state: loading / ready (now-playing + queue) / empty / error. */
    val state: StateFlow<MusicState> = _state.asStateFlow()

    // The channel the loaded snapshot belongs to, kept so controls target the same channel without re-resolving.
    private var channelId: String? = null

    /** Resolve the active channel, then load its now-playing track and upcoming queue. */
    suspend fun load() {
        _state.value = MusicState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = MusicState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        channelId = channel.id

        when (val result: ApiResult<MusicSnapshot> = musicApi.queue(channel.id)) {
            is ApiResult.Failure -> _state.value = MusicState.Error(result.error.message)
            is ApiResult.Ok -> {
                val snapshot: MusicSnapshot = result.value
                // Nothing playing and nothing queued → Empty; otherwise show whatever the provider reports.
                _state.value =
                    if (snapshot.nowPlaying == null && snapshot.queue.isEmpty()) MusicState.Empty
                    else MusicState.Ready(nowPlaying = snapshot.nowPlaying, queue = snapshot.queue)
            }
        }
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
     * play/pause from [NowPlaying.isPlaying] and offers a per-track remove on the queue.
     */
    data class Ready(
        val nowPlaying: NowPlaying?,
        val queue: List<MusicTrack>,
        val actionError: String? = null,
    ) : MusicState

    data object Empty : MusicState

    data class Error(val detail: String) : MusicState
}

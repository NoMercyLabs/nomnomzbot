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

import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BlockTrackBody
import bot.nomnomz.dashboard.core.network.BlockedTrack
import bot.nomnomz.dashboard.core.network.BlockedTrackPage
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.MusicConfig
import bot.nomnomz.dashboard.core.network.MusicSongRequestBody
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
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.songrequests_action_error

// The Song Requests page's state-holder — the channel's live queue AND every song-request-specific
// management capability: config (max queue, allowed providers, trust floor), the blocked-track list, and
// the public SR-page share link. Loads them all in parallel on [load]; controls reload on success. No
// fabricated tracks.
//
// Music and Song Requests read the SAME backend queue (`GET .../music/queue`) — Music only needs it to know
// what's now playing, so THIS controller is the one that owns viewing and mutating its membership
// (add / remove / promote / ban) and its rules (S-OBS-04).
class SongRequestsController(
    private val channelsApi: ChannelsApi,
    private val songRequestsApi: SongRequestsApi,
    // The active backend origin, read live so the pretty share link (`{origin}/sr/@name`) matches whatever
    // host served the dashboard. Null (the default, e.g. in tests) simply omits the absolute link.
    private val baseUrlProvider: () -> String? = { null },
    private val feedback: Feedback = NoOpFeedback,
) {
    private val _state: MutableStateFlow<SongRequestsState> =
        MutableStateFlow(SongRequestsState.Loading)

    /** The page render state. */
    val state: StateFlow<SongRequestsState> = _state.asStateFlow()

    // Resolved channel id — set on first load, reused by control actions so they target the same channel.
    private var channelId: String? = null

    // Last-seen track id: used by subscribeToHub to skip reload when only play/pause state changed.
    private var lastTrackId: String? = null

    /** Resolve the active channel, then load its queue, config, SR-page token, and blocked-track list. */
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

        // Blocked tracks are paged — a reload keeps the page the operator was on so an unblock doesn't jump
        // back to page 1.
        val blockedPage: Int = (_state.value as? SongRequestsState.Ready)?.blockedPage ?: 1

        coroutineScope {
            val queueDeferred = async { songRequestsApi.queue(channel.id) }
            val configDeferred = async { songRequestsApi.config(channel.id) }
            val tokenDeferred = async { songRequestsApi.srPageToken(channel.id) }
            val blockedDeferred = async { songRequestsApi.blockedTracks(channel.id, page = blockedPage) }

            val queueResult: ApiResult<List<QueuedSong>> = queueDeferred.await()
            val configResult: ApiResult<MusicConfig> = configDeferred.await()
            val tokenResult: ApiResult<String> = tokenDeferred.await()
            val blockedResult: ApiResult<BlockedTrackPage> = blockedDeferred.await()

            if (queueResult is ApiResult.Failure) {
                _state.value = SongRequestsState.Error(queueResult.error.message)
                return@coroutineScope
            }

            val queue: List<QueuedSong> = (queueResult as ApiResult.Ok).value
            val config: MusicConfig? = (configResult as? ApiResult.Ok)?.value
            val srPageToken: String? = (tokenResult as? ApiResult.Ok)?.value
            val blocked: BlockedTrackPage = (blockedResult as? ApiResult.Ok)?.value ?: BlockedTrackPage()

            // The pretty, human-shareable SR link — `{origin}/sr/@{login}` — resolvable by the public
            // by-channel route (@ + case tolerant). Only built when both the origin and the login are known.
            val shareLink: String? =
                baseUrlProvider()?.trimEnd('/')?.takeIf { it.isNotBlank() }?.let { origin ->
                    channel.login.takeIf { it.isNotBlank() }?.let { login -> "$origin/sr/@$login" }
                }

            _state.value = SongRequestsState.Ready(
                queue = queue,
                config = config,
                srPageToken = srPageToken,
                shareLink = shareLink,
                tokenUrl = buildTokenUrl(baseUrlProvider(), srPageToken),
                blockedTracks = blocked.data,
                blockedPage = blockedPage,
                blockedTotal = blocked.total,
                blockedHasMore = blocked.hasMore,
            )
        }
    }

    /**
     * Load one [page] of the blocked-track list into the Ready state (the pager's prev/next). A failure
     * announces on the shell-level feedback toast; the current rows stay put.
     */
    suspend fun loadBlockedTracks(page: Int) {
        val channel: String = channelId ?: return
        when (val result: ApiResult<BlockedTrackPage> = songRequestsApi.blockedTracks(channel, page = page)) {
            is ApiResult.Failure -> surfaceError(result.error.message)
            is ApiResult.Ok -> {
                val current: SongRequestsState = _state.value
                if (current is SongRequestsState.Ready) {
                    _state.value =
                        current.copy(
                            blockedTracks = result.value.data,
                            blockedPage = page,
                            blockedTotal = result.value.total,
                            blockedHasMore = result.value.hasMore,
                        )
                }
            }
        }
    }

    /**
     * Block a track from song requests. On success the blocked list re-reads (the new entry appears where
     * the server sorted it); on failure — including TRACK_BLOCKED when it is already on the list — the
     * error surfaces on the Ready state and the rows stay put.
     */
    suspend fun blockTrack(provider: String, trackUri: String, title: String, reason: String?) {
        val channel: String = channelId ?: return
        val body = BlockTrackBody(provider = provider, trackUri = trackUri, title = title, reason = reason)
        when (val result: ApiResult<BlockedTrack> = songRequestsApi.blockTrack(channel, body)) {
            is ApiResult.Failure -> surfaceError(result.error.message)
            is ApiResult.Ok -> loadBlockedTracks((_state.value as? SongRequestsState.Ready)?.blockedPage ?: 1)
        }
    }

    /**
     * Unblock (remove) a blocked track by its id. Re-reads the current page on success so the row drops
     * off; surfaces the error on failure. The screen gates this behind a confirmation.
     */
    suspend fun unblockTrack(blockedTrackId: String) {
        val channel: String = channelId ?: return
        when (val result: ApiResult<Unit> = songRequestsApi.unblockTrack(channel, blockedTrackId)) {
            is ApiResult.Failure -> surfaceError(result.error.message)
            is ApiResult.Ok -> loadBlockedTracks((_state.value as? SongRequestsState.Ready)?.blockedPage ?: 1)
        }
    }

    /**
     * Add a song to the queue by search [query], attributed to [requestedBy] (a manual/DJ addition).
     * Reloads on success so the new entry appears; surfaces the error without clearing the queue on failure.
     */
    suspend fun addToQueue(query: String, requestedBy: String) {
        val channel: String = channelId ?: return
        control { songRequestsApi.addToQueue(channel, MusicSongRequestBody(query, requestedBy)) }
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

    /** Move the queued song at [position] to the front of the queue — play it next. Reloads on success. */
    suspend fun promote(position: Int) = control { channel -> songRequestsApi.promote(channel, position) }

    /**
     * Ban the queued song at [position] from future song requests, removing it from the live queue too.
     * The screen gates this behind a confirmation before calling. Reloads on success.
     */
    suspend fun ban(position: Int) = control { channel -> songRequestsApi.ban(channel, position) }

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
                if (current is SongRequestsState.Ready) {
                    _state.value =
                        current.copy(
                            srPageToken = result.value,
                            tokenUrl = buildTokenUrl(baseUrlProvider(), result.value),
                        )
                }
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

    // The page is already showing content — a control failure announces on the shell-level feedback toast
    // rather than a local banner.
    private fun surfaceError(message: String) {
        if (_state.value is SongRequestsState.Ready) feedback.error(Res.string.songrequests_action_error, message)
    }
}

// The literal token-backed SR link (`{origin}/sr/{token}`) — the URL the raw token itself resolves to on the
// public route. Built purely from the resolved backend origin and the real token; null when either is missing
// (never a hardcoded scheme/host).
private fun buildTokenUrl(baseUrl: String?, token: String?): String? {
    val origin: String = baseUrl?.trimEnd('/')?.takeIf { it.isNotBlank() } ?: return null
    val safeToken: String = token?.takeIf { it.isNotBlank() } ?: return null
    return "$origin/sr/$safeToken"
}

/** The Song Requests page render state. */
sealed interface SongRequestsState {
    data object Loading : SongRequestsState

    /**
     * Loaded: the live queue, the SR config, the SR-page share link, and the blocked-track list. [config] and
     * [srPageToken] may be null when the backend call failed (resilient — the queue still renders). A
     * control-action failure announces on the shell-level feedback toast rather than a field here — see
     * [SongRequestsController.surfaceError].
     */
    data class Ready(
        val queue: List<QueuedSong>,
        val config: MusicConfig?,
        val srPageToken: String?,
        // The absolute, human-friendly public SR link (`{origin}/sr/@name`); null when the origin/login is unknown.
        val shareLink: String? = null,
        // The absolute, literal token-backed public SR link (`{origin}/sr/{token}`); null when the origin/token
        // is unknown — the URL the raw token actually resolves to, shown with its own copy affordance alongside
        // the pretty [shareLink] so a streamer without a resolvable login still gets a working link.
        val tokenUrl: String? = null,
        // The blocked song-request tracks — one page of rows plus the paging signals the section's pager needs.
        val blockedTracks: List<BlockedTrack> = emptyList(),
        val blockedPage: Int = 1,
        val blockedTotal: Int = 0,
        val blockedHasMore: Boolean = false,
    ) : SongRequestsState

    data class Error(val detail: String) : SongRequestsState
}

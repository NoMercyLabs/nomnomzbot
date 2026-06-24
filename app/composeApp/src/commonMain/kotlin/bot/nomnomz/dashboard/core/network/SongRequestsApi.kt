// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.network

import kotlinx.serialization.Serializable

// The typed song-requests facade — the channel's live music queue, sourced by the backend from the connected
// music provider (Spotify/YouTube; no fabricated tracks). The queue is a `StatusResponseDto<MusicQueueDto>`
// wrapper (`{ nowPlaying, queue: [...] }`), so it is read with getEnvelope and the upcoming `queue` list is
// exposed; the page also controls playback through the same facade. State holders depend on this interface and
// fake it in tests without HTTP.
//
// Backend routes (MusicController). The control routes are exactly the four the backend supports — there is no
// clear-queue or reorder endpoint, so the page exposes none:
//   GET    /api/v1/channels/{channelId}/music/queue            →  StatusResponseDto<MusicQueueDto>
//   POST   /api/v1/channels/{channelId}/music/skip             →  StatusResponseDto<object>
//   POST   /api/v1/channels/{channelId}/music/pause            →  StatusResponseDto<object>
//   POST   /api/v1/channels/{channelId}/music/resume           →  StatusResponseDto<object>
//   DELETE /api/v1/channels/{channelId}/music/queue/{position} →  204 No Content
// The control endpoints take no request body and report success by status code, so each goes through postUnit /
// deleteUnit (any 2xx is success); the queue is re-read after a mutating write to project the new state.
interface SongRequestsApi {
    /** The channel's upcoming song-request queue (the wrapper's `queue` list; now-playing is read elsewhere). */
    suspend fun queue(channelId: String): ApiResult<List<QueuedSong>>

    /** Skip the current track, advancing to the next queued song. */
    suspend fun skip(channelId: String): ApiResult<Unit>

    /** Pause playback (the current track stays current). */
    suspend fun pause(channelId: String): ApiResult<Unit>

    /** Resume playback after a pause. */
    suspend fun resume(channelId: String): ApiResult<Unit>

    /** Remove one queued song by its zero-based [position] (the [QueuedSong.position]). */
    suspend fun remove(channelId: String, position: Int): ApiResult<Unit>
}

class RestSongRequestsApi(private val client: ApiClient) : SongRequestsApi {

    override suspend fun queue(channelId: String): ApiResult<List<QueuedSong>> {
        // StatusResponseDto<MusicQueueDto> is the single-value `{ data: <wrapper> }` envelope, so it is read
        // with getEnvelope; the upcoming tracks are the wrapper's `queue` list.
        return when (
            val result: ApiResult<MusicQueue> =
                client.getEnvelope("api/v1/channels/$channelId/music/queue")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(result.error)
            is ApiResult.Ok -> ApiResult.Ok(result.value.queue)
        }
    }

    override suspend fun skip(channelId: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/music/skip")

    override suspend fun pause(channelId: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/music/pause")

    override suspend fun resume(channelId: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/music/resume")

    override suspend fun remove(channelId: String, position: Int): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/music/queue/$position")
}

/**
 * The full music queue (backend `MusicQueueDto`): the now-playing track and the upcoming queue. This slice is
 * the read-only upcoming list, so `nowPlaying` is modelled but unused here (ApiClient's Json ignores it cleanly
 * either way). The field names are the serialized (camelCase) names of `MusicQueueDto`.
 */
@Serializable
data class MusicQueue(
    val queue: List<QueuedSong> = emptyList(),
)

/**
 * A queued song-request (backend `QueueItemDto`): its position in the queue, the track identity, and who
 * requested it. The field names are the serialized (camelCase) names of `QueueItemDto`.
 */
@Serializable
data class QueuedSong(
    val position: Int = 0,
    val trackName: String = "",
    val artist: String = "",
    val imageUrl: String? = null,
    val durationMs: Int = 0,
    val requestedBy: String? = null,
)

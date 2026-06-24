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
// exposed. State holders depend on this interface and fake it in tests without HTTP.
//
// Backend route (MusicController):
//   GET /api/v1/channels/{channelId}/music/queue  →  StatusResponseDto<MusicQueueDto>
interface SongRequestsApi {
    /** The channel's upcoming song-request queue (the wrapper's `queue` list; now-playing is read elsewhere). */
    suspend fun queue(channelId: String): ApiResult<List<QueuedSong>>
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

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
// exposed; the page also controls playback and exposes the SR config + SR-page token management. State holders
// depend on this interface and fake it in tests without HTTP.
//
// Backend routes (MusicController):
//   GET    /api/v1/channels/{channelId}/music/queue               →  StatusResponseDto<MusicQueueDto>
//   POST   /api/v1/channels/{channelId}/music/queue                →  StatusResponseDto<object>
//   POST   /api/v1/channels/{channelId}/music/skip                →  StatusResponseDto<object>
//   POST   /api/v1/channels/{channelId}/music/pause               →  StatusResponseDto<object>
//   POST   /api/v1/channels/{channelId}/music/resume              →  StatusResponseDto<object>
//   DELETE /api/v1/channels/{channelId}/music/queue/{position}    →  204 No Content
//   POST   /api/v1/channels/{channelId}/music/queue/{position}/promote →  204 No Content
//   POST   /api/v1/channels/{channelId}/music/queue/{position}/ban →  StatusResponseDto<BlockedTrackDto>
//   GET    /api/v1/channels/{channelId}/music/config              →  StatusResponseDto<MusicConfigDto>
//   PUT    /api/v1/channels/{channelId}/music/config              →  StatusResponseDto<MusicConfigDto>
//   GET    /api/v1/channels/{channelId}/music/sr-page-token       →  StatusResponseDto<string>
//   POST   /api/v1/channels/{channelId}/music/sr-page-token/rotate →  StatusResponseDto<string>
//   GET    /api/v1/channels/{channelId}/music/blocked-tracks       →  PaginatedResponse<BlockedTrackDto>
//   POST   /api/v1/channels/{channelId}/music/blocked-tracks       →  StatusResponseDto<BlockedTrackDto>
//   DELETE /api/v1/channels/{channelId}/music/blocked-tracks/{id}  →  204 No Content
//
// This is the SAME backend queue the Music page's transport controls reload after a play/pause/skip — Song
// Requests is the page that owns VIEWING and MUTATING its membership (add/remove/promote/ban) and its rules
// (config, blocked list, SR-page share link); Music only reads it to know what's now playing.
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

    /** Add a song to the queue by search [query], attributed to [requestedBy] (a manual/DJ addition). */
    suspend fun addToQueue(channelId: String, body: MusicSongRequestBody): ApiResult<Unit>

    /** Move the queued song at [position] to the front of the queue — play it next. */
    suspend fun promote(channelId: String, position: Int): ApiResult<Unit>

    /** Ban the queued song at [position] from future song requests and remove it from the live queue. */
    suspend fun ban(channelId: String, position: Int): ApiResult<Unit>

    /** The channel's SR / music configuration. Reuses [MusicConfig] (same shape). */
    suspend fun config(channelId: String): ApiResult<MusicConfig>

    /** Update (patch) the SR / music configuration. */
    suspend fun updateConfig(channelId: String, body: UpdateMusicConfigBody): ApiResult<MusicConfig>

    /** Get (or mint) the channel's public SR-page shareable token. */
    suspend fun srPageToken(channelId: String): ApiResult<String>

    /** Rotate the SR-page token — the old share link stops working immediately. */
    suspend fun rotateSrPageToken(channelId: String): ApiResult<String>

    /** One page of the channel's blocked song-request tracks (the legacy `!bansong` list). */
    suspend fun blockedTracks(channelId: String, page: Int = 1, take: Int = 25): ApiResult<BlockedTrackPage>

    /** Block a track from song requests. Returns the created entry. */
    suspend fun blockTrack(channelId: String, body: BlockTrackBody): ApiResult<BlockedTrack>

    /** Unblock a previously blocked track by its [blockedTrackId]. */
    suspend fun unblockTrack(channelId: String, blockedTrackId: String): ApiResult<Unit>
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

    override suspend fun addToQueue(channelId: String, body: MusicSongRequestBody): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/music/queue", body)

    override suspend fun promote(channelId: String, position: Int): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/music/queue/$position/promote")

    override suspend fun ban(channelId: String, position: Int): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/music/queue/$position/ban")

    override suspend fun config(channelId: String): ApiResult<MusicConfig> =
        client.getEnvelope("api/v1/channels/$channelId/music/config")

    override suspend fun updateConfig(channelId: String, body: UpdateMusicConfigBody): ApiResult<MusicConfig> =
        client.putEnvelope("api/v1/channels/$channelId/music/config", body)

    override suspend fun srPageToken(channelId: String): ApiResult<String> =
        client.getEnvelope("api/v1/channels/$channelId/music/sr-page-token")

    override suspend fun rotateSrPageToken(channelId: String): ApiResult<String> =
        client.postEnvelope("api/v1/channels/$channelId/music/sr-page-token/rotate", Unit)

    // The list is a PaginatedResponse (flat `{ data, total, hasMore, ... }`) — getDirect reads the whole body,
    // same as the Music page's own blocked-track read (this is the same backend list).
    override suspend fun blockedTracks(channelId: String, page: Int, take: Int): ApiResult<BlockedTrackPage> =
        client.getDirect("api/v1/channels/$channelId/music/blocked-tracks?page=$page&take=$take")

    // The create echoes the new entry in a StatusResponseDto<BlockedTrackDto> envelope — postEnvelope unwraps it.
    override suspend fun blockTrack(channelId: String, body: BlockTrackBody): ApiResult<BlockedTrack> =
        client.postEnvelope("api/v1/channels/$channelId/music/blocked-tracks", body)

    override suspend fun unblockTrack(channelId: String, blockedTrackId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/music/blocked-tracks/$blockedTrackId")
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
 * requested it. The field names are the serialized (camelCase) names of `QueueItemDto`. [cost] is the
 * channel-currency amount the requester paid (0 for a free/unpaid request) — a paid entry (`cost > 0`)
 * gets a "paid" indicator on the queue row.
 */
@Serializable
data class QueuedSong(
    val position: Int = 0,
    val trackName: String = "",
    val artist: String = "",
    val imageUrl: String? = null,
    val durationMs: Int = 0,
    val requestedBy: String? = null,
    val cost: Int = 0,
)

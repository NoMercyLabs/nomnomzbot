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

// The typed music facade — the channel's live playback: the now-playing track AND the upcoming queue, sourced
// by the backend from the connected music provider (Spotify/YouTube; no fabricated tracks). Unlike the
// song-requests facade (which is the queue-only management view and discards now-playing), this one reads the
// full `MusicQueueDto` and surfaces BOTH halves, then drives playback. State holders depend on this interface
// and fake it in tests without HTTP.
//
// Backend routes (MusicController). The control surface is exactly what the controller exposes — there is no
// volume, previous, reorder, or clear-queue HTTP route (the service's SetVolumeAsync is not surfaced), so the
// page offers none of those and never invents one:
//   GET    /api/v1/channels/{channelId}/music/queue            →  StatusResponseDto<MusicQueueDto>
//   POST   /api/v1/channels/{channelId}/music/skip             →  StatusResponseDto<object>
//   POST   /api/v1/channels/{channelId}/music/pause            →  StatusResponseDto<object>
//   POST   /api/v1/channels/{channelId}/music/resume           →  StatusResponseDto<object>
//   DELETE /api/v1/channels/{channelId}/music/queue/{position} →  204 No Content
// `queue` carries the whole `MusicQueueDto` (now-playing + upcoming) in one read, so the page renders both from
// a single call; the control routes take no body and report success by status code (any 2xx), so each goes
// through postUnit / deleteUnit and the queue is re-read after a mutating write to project the new state.
interface MusicApi {
    /** The channel's full playback snapshot: the now-playing track (or null) and the upcoming queue. */
    suspend fun queue(channelId: String): ApiResult<MusicSnapshot>

    /** Skip the current track, advancing to the next queued song. */
    suspend fun skip(channelId: String): ApiResult<Unit>

    /** Pause playback (the current track stays current). */
    suspend fun pause(channelId: String): ApiResult<Unit>

    /** Resume (start) playback after a pause. */
    suspend fun resume(channelId: String): ApiResult<Unit>

    /** Remove one queued song by its zero-based [position] (the [MusicTrack.position]). */
    suspend fun remove(channelId: String, position: Int): ApiResult<Unit>

    /** Add a song to the queue by search [query], attributed to [requestedBy]. */
    suspend fun addToQueue(channelId: String, body: MusicSongRequestBody): ApiResult<Unit>

    /** The channel's SR / music configuration. */
    suspend fun config(channelId: String): ApiResult<MusicConfig>

    /** Update (patch) the SR / music configuration. */
    suspend fun updateConfig(channelId: String, body: UpdateMusicConfigBody): ApiResult<MusicConfig>

    /** Get (or mint) the channel's public SR-page shareable token. */
    suspend fun srPageToken(channelId: String): ApiResult<String>

    /** Rotate the SR-page token — the old share link stops working immediately. */
    suspend fun rotateSrPageToken(channelId: String): ApiResult<String>
}

class RestMusicApi(private val client: ApiClient) : MusicApi {

    override suspend fun queue(channelId: String): ApiResult<MusicSnapshot> =
        // StatusResponseDto<MusicQueueDto> is the single-value `{ data: <wrapper> }` envelope, so it is read
        // with getEnvelope; the wrapper carries both `nowPlaying` and the upcoming `queue` list.
        client.getEnvelope("api/v1/channels/$channelId/music/queue")

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

    override suspend fun config(channelId: String): ApiResult<MusicConfig> =
        client.getEnvelope("api/v1/channels/$channelId/music/config")

    override suspend fun updateConfig(channelId: String, body: UpdateMusicConfigBody): ApiResult<MusicConfig> =
        client.putEnvelope("api/v1/channels/$channelId/music/config", body)

    // The token is a bare string value inside StatusResponseDto<string> — getEnvelope<String> unwraps it.
    override suspend fun srPageToken(channelId: String): ApiResult<String> =
        client.getEnvelope("api/v1/channels/$channelId/music/sr-page-token")

    override suspend fun rotateSrPageToken(channelId: String): ApiResult<String> =
        client.postEnvelope("api/v1/channels/$channelId/music/sr-page-token/rotate", Unit)
}

/**
 * The full music snapshot (backend `MusicQueueDto`): the now-playing track and the upcoming queue. The field
 * names are the serialized (camelCase) names of `MusicQueueDto`. `nowPlaying` is null when nothing is playing.
 */
@Serializable
data class MusicSnapshot(
    val nowPlaying: NowPlaying? = null,
    val queue: List<MusicTrack> = emptyList(),
)

/**
 * The now-playing track (backend `NowPlayingDto`): the track identity, its art, the playback progress, whether
 * it is currently playing, the (provider-reported) volume, who requested it, and the source provider. The
 * field names are the serialized (camelCase) names of `NowPlayingDto`. `volume` is read-only context here —
 * the backend exposes no volume-control route, so the page shows it but does not let the user change it.
 */
@Serializable
data class NowPlaying(
    val trackName: String? = null,
    val artist: String? = null,
    val album: String? = null,
    val imageUrl: String? = null,
    val durationMs: Int = 0,
    val progressMs: Int = 0,
    val isPlaying: Boolean = false,
    val volume: Int = 0,
    val requestedBy: String? = null,
    val provider: String = "",
)

/**
 * A queued track (backend `QueueItemDto`): its position in the queue, the track identity, and who requested
 * it. The field names are the serialized (camelCase) names of `QueueItemDto`.
 */
@Serializable
data class MusicTrack(
    val position: Int = 0,
    val trackName: String = "",
    val artist: String = "",
    val imageUrl: String? = null,
    val durationMs: Int = 0,
    val requestedBy: String? = null,
)

/** Add a song request to the queue (backend `SongRequestDto`). */
@Serializable
data class MusicSongRequestBody(val query: String, val requestedBy: String)

/** SR / music configuration (backend `MusicConfigDto`). */
@Serializable
data class MusicConfig(
    val isEnabled: Boolean = true,
    val preferredProvider: String = "auto",
    val maxQueueSize: Int = 50,
    val maxRequestsPerUser: Int = 3,
    val allowYouTube: Boolean = true,
    val allowSpotify: Boolean = true,
    val minTrustLevel: String = "everyone",
)

/** Partial update body (backend `UpdateMusicConfigDto`). All fields optional — null = don't change. */
@Serializable
data class UpdateMusicConfigBody(
    val isEnabled: Boolean? = null,
    val preferredProvider: String? = null,
    val maxQueueSize: Int? = null,
    val maxRequestsPerUser: Int? = null,
    val allowYouTube: Boolean? = null,
    val allowSpotify: Boolean? = null,
    val minTrustLevel: String? = null,
)

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

    // ── Extended remote controls (Spotify-specific; return Unit on success) ──────

    /** Seek to [positionMs] in the current track. */
    suspend fun seek(channelId: String, positionMs: Int): ApiResult<Unit>

    /** Enable or disable shuffle. */
    suspend fun setShuffle(channelId: String, enabled: Boolean): ApiResult<Unit>

    /** Set repeat mode: "off", "track", or "context". */
    suspend fun setRepeat(channelId: String, mode: String): ApiResult<Unit>

    /** Transfer playback to another device. */
    suspend fun transferPlayback(channelId: String, deviceId: String, play: Boolean = false): ApiResult<Unit>

    /** Return available playback devices. */
    suspend fun getDevices(channelId: String): ApiResult<List<MusicDevice>>

    /** Return user playlists. */
    suspend fun getPlaylists(channelId: String, offset: Int = 0, limit: Int = 20): ApiResult<List<MusicPlaylist>>

    /** Start playback of a playlist or album by URI. */
    suspend fun playContext(channelId: String, contextUri: String): ApiResult<Unit>

    // ── Blocked tracks (the legacy `!bansong` list) ──────────────────────────────

    /** One page of the channel's blocked song-request tracks. */
    suspend fun blockedTracks(channelId: String, page: Int = 1, take: Int = 25): ApiResult<BlockedTrackPage>

    /** Block a track from song requests. Returns the created entry. */
    suspend fun blockTrack(channelId: String, body: BlockTrackBody): ApiResult<BlockedTrack>

    /** Unblock a previously blocked track by its [blockedTrackId]. */
    suspend fun unblockTrack(channelId: String, blockedTrackId: String): ApiResult<Unit>
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

    override suspend fun seek(channelId: String, positionMs: Int): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/music/seek", SeekBody(positionMs))

    override suspend fun setShuffle(channelId: String, enabled: Boolean): ApiResult<Unit> =
        client.patchUnit("api/v1/channels/$channelId/music/shuffle", ShuffleBody(enabled))

    override suspend fun setRepeat(channelId: String, mode: String): ApiResult<Unit> =
        client.patchUnit("api/v1/channels/$channelId/music/repeat", RepeatBody(mode))

    override suspend fun transferPlayback(channelId: String, deviceId: String, play: Boolean): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/music/transfer", MusicTransferBody(deviceId, play))

    override suspend fun getDevices(channelId: String): ApiResult<List<MusicDevice>> =
        client.getEnvelope("api/v1/channels/$channelId/music/devices")

    override suspend fun getPlaylists(channelId: String, offset: Int, limit: Int): ApiResult<List<MusicPlaylist>> =
        client.getEnvelope("api/v1/channels/$channelId/music/playlists?offset=$offset&limit=$limit")

    override suspend fun playContext(channelId: String, contextUri: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/music/play-context", PlayContextBody(contextUri))

    // The list is a PaginatedResponse (flat `{ data, total, hasMore, ... }`) — getDirect reads the whole body,
    // same as the TTS voice catalogue; `page`/`take` is the shared paging convention.
    override suspend fun blockedTracks(channelId: String, page: Int, take: Int): ApiResult<BlockedTrackPage> =
        client.getDirect("api/v1/channels/$channelId/music/blocked-tracks?page=$page&take=$take")

    // The create echoes the new entry in a StatusResponseDto<BlockedTrackDto> envelope — postEnvelope unwraps it.
    override suspend fun blockTrack(channelId: String, body: BlockTrackBody): ApiResult<BlockedTrack> =
        client.postEnvelope("api/v1/channels/$channelId/music/blocked-tracks", body)

    override suspend fun unblockTrack(channelId: String, blockedTrackId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/music/blocked-tracks/$blockedTrackId")
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
    // The live player toggles the remote controls render (backend NowPlayingDto): shuffle on/off, and the
    // repeat mode ("off" | "track" | "context"). false/"off" on providers that do not report them.
    val shuffleState: Boolean = false,
    val repeatState: String = "off",
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

// ── Remote control request bodies ────────────────────────────────────────────

@Serializable
internal data class SeekBody(val positionMs: Int)

@Serializable
internal data class ShuffleBody(val enabled: Boolean)

@Serializable
internal data class RepeatBody(val mode: String)

@Serializable
internal data class MusicTransferBody(val deviceId: String, val play: Boolean = false)

@Serializable
internal data class PlayContextBody(val contextUri: String)

// ── Remote control response models ───────────────────────────────────────────

/** A playback device (backend `MusicDeviceDto`). */
@Serializable
data class MusicDevice(
    val id: String = "",
    val name: String = "",
    val type: String = "",
    val isActive: Boolean = false,
    val volumePercent: Int = 0,
)

/** A user playlist (backend `MusicPlaylistDto`). */
@Serializable
data class MusicPlaylist(
    val id: String = "",
    val name: String = "",
    val uri: String = "",
    val trackCount: Int = 0,
    val imageUrl: String? = null,
)

// ── Blocked tracks ───────────────────────────────────────────────────────────

/**
 * A channel's blocked song-request track (backend `BlockedTrackDto` — the legacy `!bansong` list entry): the
 * provider + track URI it matches on, the human-readable [title], the optional block [reason], and when/who
 * blocked it. The field names are the serialized (camelCase) names of `BlockedTrackDto`.
 */
@Serializable
data class BlockedTrack(
    val id: String = "",
    val provider: String = "",
    val trackUri: String = "",
    val title: String = "",
    val reason: String? = null,
    val blockedByUserId: String? = null,
    val createdAt: String = "",
)

/**
 * One page of the blocked-track list (backend `PaginatedResponse<BlockedTrackDto>`): the [data] rows plus the
 * paging signals — [total] result count and [hasMore] (another page exists after this one).
 */
@Serializable
data class BlockedTrackPage(
    val data: List<BlockedTrack> = emptyList(),
    val total: Int = 0,
    val hasMore: Boolean = false,
    val nextPage: Int? = null,
)

/** Block a track from song requests (backend `BlockTrackRequest`). [title] labels the entry in the list. */
@Serializable
data class BlockTrackBody(
    val provider: String,
    val trackUri: String,
    val title: String,
    val reason: String? = null,
)

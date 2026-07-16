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

import bot.nomnomz.dashboard.core.io.AudioFile
import io.ktor.http.ContentType
import kotlinx.serialization.Serializable

// The typed sound-clip facade — the channel's broadcaster-uploaded audio clips (spec §3). All real data from
// the backend; no fabricated rows. The state holder depends on this interface and fakes it in tests.
//
// Backend routes (SoundClipsController):
//   GET    /api/v1/sound-clips                →  PaginatedResponse<SoundClipDto>  (name order)
//   GET    /api/v1/sound-clips/{id}           →  StatusResponseDto<SoundClipDto>
//   POST   /api/v1/sound-clips                →  StatusResponseDto<SoundClipDto> (201, multipart)
//   PUT    /api/v1/sound-clips/{id}           →  StatusResponseDto<SoundClipDto>
//   DELETE /api/v1/sound-clips/{id}           →  204
//   POST   /api/v1/sound-clips/{id}/preview   →  204  (pushes PlaySound to overlay)
interface SoundApi {
    /** The channel's sound clips, alphabetically by name slug. */
    suspend fun list(): ApiResult<List<SoundClip>>

    /** Update a clip's display name, volume, or enabled state. */
    suspend fun update(id: String, body: UpdateSoundClipBody): ApiResult<Unit>

    /** Delete a clip by its UUID. */
    suspend fun delete(id: String): ApiResult<Unit>

    /** Preview a clip on the overlay (pushes PlaySound via SignalR). */
    suspend fun preview(id: String): ApiResult<Unit>

    /**
     * Upload a new audio [file] as multipart/form-data. [name] is the pipeline-action slug; [displayName] is
     * the human-readable label shown in the UI; [defaultVolume] is the initial playback volume (0–100).
     */
    suspend fun upload(
        name: String,
        displayName: String,
        defaultVolume: Int,
        file: AudioFile,
    ): ApiResult<SoundClip>
}

class RestSoundApi(private val client: ApiClient) : SoundApi {
    override suspend fun list(): ApiResult<List<SoundClip>> =
        when (val page: ApiResult<PaginatedEnvelope<SoundClip>> =
            client.getDirect("api/v1/sound-clips?page=1&pageSize=100")) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }

    override suspend fun update(id: String, body: UpdateSoundClipBody): ApiResult<Unit> =
        client.putUnit("api/v1/sound-clips/$id", body)

    override suspend fun delete(id: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/sound-clips/$id")

    override suspend fun preview(id: String): ApiResult<Unit> =
        client.postUnit("api/v1/sound-clips/$id/preview", Unit)

    override suspend fun upload(
        name: String,
        displayName: String,
        defaultVolume: Int,
        file: AudioFile,
    ): ApiResult<SoundClip> =
        client.postMultipartWithFields(
            path = "api/v1/sound-clips",
            fileFieldName = "file",
            fileName = file.name,
            fileBytes = file.bytes,
            fileContentType = runCatching { ContentType.parse(file.mimeType) }
                .getOrDefault(ContentType.Audio.Any),
            fields =
                mapOf(
                    "name" to name,
                    "displayName" to displayName,
                    "defaultVolume" to defaultVolume.toString(),
                ),
        )
}

/**
 * A sound clip (backend `SoundClipDto`): [id] is the UUID, [name] is the slug used in pipeline actions, and
 * [displayName] is the human-readable label. [durationMs] is 0 when the server could not probe the file.
 */
@Serializable
data class SoundClip(
    val id: String = "",
    val name: String = "",
    val displayName: String = "",
    val mimeType: String = "",
    val durationMs: Int = 0,
    val sizeBytes: Long = 0L,
    val defaultVolume: Int = 80,
    val isEnabled: Boolean = true,
    val createdAt: String = "",
    // Relative, anonymous stream URL the dashboard plays for an in-browser Preview (backend SoundClipDto.previewUrl).
    val previewUrl: String = "",
)

/** The update-clip request body (backend `UpdateSoundClipRequest`). */
@Serializable
data class UpdateSoundClipBody(
    val displayName: String,
    val defaultVolume: Int,
    val isEnabled: Boolean,
)

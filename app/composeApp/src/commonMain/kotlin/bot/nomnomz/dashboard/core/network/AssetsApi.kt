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

import bot.nomnomz.dashboard.core.io.AssetFile
import io.ktor.http.ContentType
import kotlinx.serialization.Serializable

// The typed asset-library facade — the channel's broadcaster-uploaded media (images/audio for overlays and
// widgets), the Sound Clips twin. All real data from the backend; no fabricated rows. The state holder
// depends on this interface and fakes it in tests.
//
// Backend routes (AssetsController):
//   GET    /api/v1/assets            →  PaginatedResponse<ChannelAssetDto>
//   POST   /api/v1/assets            →  StatusResponseDto<ChannelAssetDto> (multipart; same name = replace)
//   DELETE /api/v1/assets/{id}       →  StatusResponseDto<bool>
//   GET    {dto.url}                 →  the file itself (anonymous — the OBS/widget serving URL)
interface AssetsApi {
    /** The channel's assets, in backend order. */
    suspend fun list(): ApiResult<List<ChannelAsset>>

    /**
     * Upload a media [file] as multipart/form-data. [name] is the stable slug widgets reference — uploading
     * the same name again REPLACES that asset in place; [displayName] is the human-readable label.
     */
    suspend fun upload(name: String, displayName: String, file: AssetFile): ApiResult<ChannelAsset>

    /** Delete an asset by its UUID. */
    suspend fun delete(id: String): ApiResult<Unit>
}

class RestAssetsApi(private val client: ApiClient) : AssetsApi {
    override suspend fun list(): ApiResult<List<ChannelAsset>> =
        when (val page: ApiResult<PaginatedEnvelope<ChannelAsset>> =
            client.getDirect("api/v1/assets?page=1&take=200")) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }

    override suspend fun upload(
        name: String,
        displayName: String,
        file: AssetFile,
    ): ApiResult<ChannelAsset> =
        client.postMultipartWithFields(
            path = "api/v1/assets",
            fileFieldName = "File",
            fileName = file.name,
            fileBytes = file.bytes,
            fileContentType = runCatching { ContentType.parse(file.mimeType) }
                .getOrDefault(ContentType.Application.OctetStream),
            fields =
                mapOf(
                    "Name" to name,
                    "DisplayName" to displayName,
                ),
        )

    override suspend fun delete(id: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/assets/$id")
}

/**
 * A channel media asset (backend `ChannelAssetDto`): [id] is the UUID, [name] is the stable slug overlays
 * and widgets reference (uploading the same name replaces the file), [kind] is `image` or `audio`, and
 * [url] is the RELATIVE anonymous serving URL (`/api/v1/assets/file/{channelId}/{name}?v=…`) — prefix it
 * with the active backend base URL for an absolute OBS/browser-source link.
 */
@Serializable
data class ChannelAsset(
    val id: String = "",
    val name: String = "",
    val displayName: String = "",
    val kind: String = "",
    val mimeType: String = "",
    val sizeBytes: Long = 0L,
    val createdAt: String = "",
    val url: String = "",
)

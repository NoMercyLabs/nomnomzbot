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

// The channel side of platform templates (PlatformTemplatesController): browse the published catalogue for
// one kind and install a copy into the channel. Install is gated server-side by the kind's own write key.

import kotlinx.serialization.Serializable

/** One published platform template (PlatformTemplateDto). [payloadJson] is the kind-shaped template body. */
@Serializable
data class PlatformTemplate(
    val definitionId: String,
    val kind: String,
    val key: String,
    val displayName: String,
    val description: String? = null,
    val version: Int,
    val payloadJson: String,
)

/** The channel row an install created or replaced (InstalledPlatformTemplateDto). */
@Serializable
data class InstalledPlatformTemplate(
    val kind: String,
    val entityId: String,
    val name: String,
)

/** Install options (InstallPlatformTemplateRequest). [pipelineId] must be one of this channel's pipelines. */
@Serializable
data class InstallPlatformTemplateBody(val pipelineId: String? = null)

interface PlatformTemplatesApi {
    suspend fun list(channelId: String, kind: String): ApiResult<List<PlatformTemplate>>

    suspend fun install(
        channelId: String,
        definitionId: String,
        body: InstallPlatformTemplateBody,
    ): ApiResult<InstalledPlatformTemplate>
}

class PlatformTemplatesApiImpl(private val client: ApiClient) : PlatformTemplatesApi {
    override suspend fun list(channelId: String, kind: String): ApiResult<List<PlatformTemplate>> =
        client.getAllPages { page ->
            "api/v1/channels/$channelId/platform-templates?kind=${kind.encodeQuery()}&page=$page&pageSize=100"
        }

    override suspend fun install(
        channelId: String,
        definitionId: String,
        body: InstallPlatformTemplateBody,
    ): ApiResult<InstalledPlatformTemplate> =
        client.postEnvelope("api/v1/channels/$channelId/platform-templates/$definitionId/install", body)
}

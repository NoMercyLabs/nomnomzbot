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

// "Restore default" for one channel row that came from the platform: a seeded system pipeline (the raid flows)
// or a timer installed from a template. Backend routes (PipelinesController / TimersController):
//   GET  /api/v1/channels/{channelId}/{resource}/{id}/platform-default          → what a restore would change
//   POST /api/v1/channels/{channelId}/{resource}/{id}/platform-default/restore  → restore, returns the fresh preview
interface RestoreDefaultsApi {
    suspend fun preview(channelId: String, resource: RestoreDefaultResource, id: String): ApiResult<PlatformDefaultPreview>

    suspend fun restore(channelId: String, resource: RestoreDefaultResource, id: String): ApiResult<PlatformDefaultPreview>
}

/** The channel resources that can go back to a platform default, by their route segment. */
enum class RestoreDefaultResource(val path: String) {
    Pipelines("pipelines"),
    Timers("timers"),
}

class RestRestoreDefaultsApi(private val client: ApiClient) : RestoreDefaultsApi {
    override suspend fun preview(
        channelId: String,
        resource: RestoreDefaultResource,
        id: String,
    ): ApiResult<PlatformDefaultPreview> =
        client.getEnvelope("api/v1/channels/$channelId/${resource.path}/$id/platform-default")

    override suspend fun restore(
        channelId: String,
        resource: RestoreDefaultResource,
        id: String,
    ): ApiResult<PlatformDefaultPreview> =
        client.postEnvelope("api/v1/channels/$channelId/${resource.path}/$id/platform-default/restore")
}

/**
 * The consequence of restoring one row (backend `PlatformDefaultPreviewDto`): the default's [defaultName], the
 * version the row came from ([installedVersion]) and the one a restore writes ([defaultVersion]), whether the
 * channel changed it ([isEdited]), and every part that differs ([changes]). Empty [changes] = nothing to restore.
 */
@Serializable
data class PlatformDefaultPreview(
    val kind: String = "",
    val rowId: String = "",
    val defaultName: String = "",
    val installedVersion: Int? = null,
    val defaultVersion: Int = 0,
    val isEdited: Boolean = false,
    val changes: List<PlatformDefaultChange> = emptyList(),
)

/** One part a restore replaces (backend `PlatformDefaultChangeDto`); [field] is a stable key the dashboard labels. */
@Serializable
data class PlatformDefaultChange(val field: String = "", val current: String = "", val default: String = "")

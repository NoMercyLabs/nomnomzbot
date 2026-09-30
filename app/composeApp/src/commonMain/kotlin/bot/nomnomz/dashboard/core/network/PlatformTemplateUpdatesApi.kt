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

// Template updates, channel side (PlatformTemplatesController `updates` + `copies/{rowId}/update`): which of this
// channel's installed copies sit behind their template's published version, and taking one update. The update is
// gated server-side by the kind's own write key.

import kotlinx.serialization.Serializable

/**
 * One installed copy that is behind its template (PlatformTemplateUpdateDto). [rowId] is the channel row (a
 * timer id, a pick-list id, …). [editedSinceInstall] is true when the channel changed the copy after install:
 * taking the update replaces those edits. [installedVersion] is null for a copy installed before versions were
 * stamped.
 */
@Serializable
data class PlatformTemplateUpdate(
    val rowId: String,
    val definitionId: String,
    val kind: String,
    val displayName: String,
    val installedVersion: Int? = null,
    val currentVersion: Int,
    val editedSinceInstall: Boolean,
)

interface PlatformTemplateUpdatesApi {
    /** This channel's copies of [kind] whose template has a newer published version. */
    suspend fun list(channelId: String, kind: String): ApiResult<List<PlatformTemplateUpdate>>

    /** Rewrites copy [rowId] from template [definitionId]'s current version. Replaces the channel's own edits. */
    suspend fun apply(channelId: String, definitionId: String, rowId: String): ApiResult<PlatformTemplateUpdate>
}

class PlatformTemplateUpdatesApiImpl(private val client: ApiClient) : PlatformTemplateUpdatesApi {
    override suspend fun list(channelId: String, kind: String): ApiResult<List<PlatformTemplateUpdate>> =
        client.getEnvelope("api/v1/channels/$channelId/platform-templates/updates?kind=${kind.encodeQuery()}")

    override suspend fun apply(
        channelId: String,
        definitionId: String,
        rowId: String,
    ): ApiResult<PlatformTemplateUpdate> =
        client.postEnvelope("api/v1/channels/$channelId/platform-templates/$definitionId/copies/$rowId/update")
}

// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.platformtemplates.state

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.PlatformTemplateUpdate
import bot.nomnomz.dashboard.core.network.PlatformTemplateUpdatesApi

/**
 * Behaves like the backend: [list] returns the copies still behind, and a successful [apply] brings that copy
 * current, so it drops out of the next [list]. [applyFailure] makes every apply fail and leaves the copy behind.
 */
class FakePlatformTemplateUpdatesApi(
    behind: List<PlatformTemplateUpdate>,
    var applyFailure: ApiError? = null,
) : PlatformTemplateUpdatesApi {
    private val stillBehind: MutableList<PlatformTemplateUpdate> = behind.toMutableList()

    val listed: MutableList<Pair<String, String>> = mutableListOf()
    val applied: MutableList<Triple<String, String, String>> = mutableListOf()

    override suspend fun list(channelId: String, kind: String): ApiResult<List<PlatformTemplateUpdate>> {
        listed += channelId to kind
        return ApiResult.Ok(stillBehind.filter { it.kind == kind })
    }

    override suspend fun apply(
        channelId: String,
        definitionId: String,
        rowId: String,
    ): ApiResult<PlatformTemplateUpdate> {
        applied += Triple(channelId, definitionId, rowId)
        applyFailure?.let { return ApiResult.Failure(it) }
        val update: PlatformTemplateUpdate =
            stillBehind.firstOrNull { it.rowId == rowId && it.definitionId == definitionId }
                ?: return ApiResult.Failure(ApiError(404, "NOT_FOUND", "no installed copy"))
        stillBehind.remove(update)
        return ApiResult.Ok(
            update.copy(installedVersion = update.currentVersion, editedSinceInstall = false),
        )
    }
}

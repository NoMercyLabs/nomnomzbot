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

// The admin-plane editor for platform defaults (plan item A4, /api/v1/admin/platform-defaults/*). Gated
// server-side on `platform:defaults:manage`. Every family follows one shape: list the defaults, preview the
// counted blast radius of a change, then save — the save echoes the previewed count back and the server
// refuses it (`PREVIEW_STALE`) when the live count moved, and returns the row read back from the store.

import kotlinx.serialization.Serializable

/** The counted consequence of changing one platform default (backend `PlatformDefaultBlastRadiusDto`). */
@Serializable
data class PlatformDefaultBlastRadius(
    val channelsAffected: Int = 0,
    val channelsKeepingOwnSetting: Int = 0,
    val sampleChannelNames: List<String> = emptyList(),
    val requiresDangerConfirmation: Boolean = false,
)

/**
 * One gateable action's platform default (backend `ActionDefaultDto`). Levels are unified-ladder rung values;
 * the screen renders them as role NAMES only. [floorTier] is the backend `DangerTier` ordinal
 * (Critical = 0, Tos = 1, Low = 2).
 */
@Serializable
data class ActionDefault(
    val actionKey: String,
    val plane: Int = 0,
    val description: String? = null,
    val shippedDefaultLevel: Int = 0,
    val platformDefaultLevel: Int? = null,
    val effectiveDefaultLevel: Int = 0,
    val floorLevel: Int = 0,
    val floorTier: Int = ActionDangerTier.LOW,
    val channelOverrideCount: Int = 0,
)

object ActionDangerTier {
    const val CRITICAL: Int = 0
    const val TOS: Int = 1
    const val LOW: Int = 2
}

/** Sets ([level]) or clears (null) an action's platform default (backend `SetActionDefaultRequest`). */
@Serializable
data class SetActionDefaultRequest(
    val level: Int?,
    val confirmedChannelsAffected: Int,
    val confirmDanger: Boolean,
)

interface PlatformDefaultsApi {
    suspend fun actionDefaults(): ApiResult<List<ActionDefault>>

    /** The counted blast radius of moving [actionKey]'s default to [level] (null = back to the shipped default). */
    suspend fun previewActionDefault(actionKey: String, level: Int?): ApiResult<PlatformDefaultBlastRadius>

    /** Applies the change and returns the row the server read back after saving. */
    suspend fun setActionDefault(actionKey: String, body: SetActionDefaultRequest): ApiResult<ActionDefault>
}

class RestPlatformDefaultsApi(private val client: ApiClient) : PlatformDefaultsApi {
    override suspend fun actionDefaults(): ApiResult<List<ActionDefault>> =
        client.getEnvelope("api/v1/admin/platform-defaults/actions")

    override suspend fun previewActionDefault(
        actionKey: String,
        level: Int?,
    ): ApiResult<PlatformDefaultBlastRadius> {
        val query: String = if (level == null) "" else "?level=$level"
        return client.getEnvelope(
            "api/v1/admin/platform-defaults/actions/${actionKey.encodeQuery()}/blast-radius$query",
        )
    }

    override suspend fun setActionDefault(
        actionKey: String,
        body: SetActionDefaultRequest,
    ): ApiResult<ActionDefault> =
        client.putEnvelope("api/v1/admin/platform-defaults/actions/${actionKey.encodeQuery()}", body)
}

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

// The typed built-in-commands facade — the platform-defined commands (music !sr/!skip/!queue/!volume/!song)
// the Commands page shows in a separate section. The backend controls the catalogue; the dashboard can only
// toggle each builtin on/off per channel. Real data only — no fabricated rows.
//
// Backend routes (BuiltinsController):
//   GET   /api/v1/channels/{channelId}/builtins               → StatusResponseDto<List<BuiltinCommand>>
//   PATCH /api/v1/channels/{channelId}/builtins/{builtinKey}  → StatusResponseDto<Unit> (toggle enabled)
interface BuiltinsApi {
    /** Lists all platform-defined built-in commands for the channel, with their enabled state. */
    suspend fun list(channelId: String): ApiResult<List<BuiltinCommand>>

    /** Enable or disable a single builtin by its [builtinKey] (e.g. "sr", "skip"). */
    suspend fun setEnabled(channelId: String, builtinKey: String, enabled: Boolean): ApiResult<Unit>
}

class RestBuiltinsApi(private val client: ApiClient) : BuiltinsApi {
    // The list is a StatusResponseDto (envelope with `data: [...]`), not a PaginatedResponse, so it is read
    // with getEnvelope which unwraps the `data` field.
    override suspend fun list(channelId: String): ApiResult<List<BuiltinCommand>> =
        client.getEnvelope("api/v1/channels/$channelId/builtins")

    override suspend fun setEnabled(
        channelId: String,
        builtinKey: String,
        enabled: Boolean,
    ): ApiResult<Unit> =
        client.patchUnit(
            "api/v1/channels/$channelId/builtins/$builtinKey",
            SetBuiltinEnabledBody(enabled),
        )
}

/**
 * A platform-defined built-in command (backend `BuiltinCommandDto`): what it is ([builtinKey] / [name]),
 * whether it is enabled for this channel ([isEnabled]), and its defaults.
 */
@Serializable
data class BuiltinCommand(
    val builtinKey: String = "",
    val name: String = "",
    val isEnabled: Boolean = true,
    val defaultCooldownSeconds: Int = 0,
    val defaultMinPermissionLevel: Int = 0,
)

/** Toggle request body (backend `SetBuiltinEnabledRequest`). */
@Serializable
private data class SetBuiltinEnabledBody(val enabled: Boolean)

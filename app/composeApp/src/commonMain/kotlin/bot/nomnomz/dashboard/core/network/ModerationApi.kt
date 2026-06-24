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

// The typed moderation facade. This slice renders the channel's currently-banned viewers (read-only); the
// list comes straight from the real Twitch-backed moderation state on the backend (no fabricated entries).
// State holders depend on the interface (the existing "depend on interfaces" convention) and fake it in
// tests without HTTP.
//
// Backend route (CommunityController):
//   GET /api/v1/channels/{channelId}/community/bans  →  PaginatedResponse<BannedUserDto>
// A PaginatedResponse is a flat `{ data: [...] }` object (not the single-value StatusResponseDto envelope),
// so it is read with getDirect + PaginatedEnvelope rather than getEnvelope.
interface ModerationApi {
    /** The channel's currently-banned viewers — most recent ban first. */
    suspend fun bans(channelId: String): ApiResult<List<BannedUser>>
}

class RestModerationApi(private val client: ApiClient) : ModerationApi {
    override suspend fun bans(channelId: String): ApiResult<List<BannedUser>> =
        when (
            val page: ApiResult<PaginatedEnvelope<BannedUser>> =
                client.getDirect("api/v1/channels/$channelId/community/bans?page=1&pageSize=25")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }
}

/**
 * One banned viewer (backend `BannedUserDto`). Fields mirror the backend record's camelCase JSON exactly
 * (the contract test guards this): the Twitch id, login/display name, an optional avatar, the ban reason,
 * who issued it, and when.
 */
@Serializable
data class BannedUser(
    val id: String,
    val username: String = "",
    val displayName: String = "",
    val profileImageUrl: String? = null,
    val reason: String = "",
    val bannedBy: String = "",
    val bannedAt: String = "",
)

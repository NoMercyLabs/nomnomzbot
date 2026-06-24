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

// The typed community facade — the channel's real viewers, sourced from the Twitch API + chat history by the
// backend (no fabricated viewer lists). The list is a `PaginatedResponse<CommunityUserDto>` (a flat
// `{ data: [...] }`), so it is read with getDirect like the channel list. State holders depend on this
// interface and fake it in tests without HTTP.
//
// Backend route (CommunityController):
//   GET /api/v1/channels/{channelId}/community  →  PaginatedResponse<CommunityUserDto>
interface CommunityApi {
    /** The channel's community — the first page of viewers (chatters + mods) the backend resolves. */
    suspend fun members(channelId: String): ApiResult<List<CommunityMember>>
}

class RestCommunityApi(private val client: ApiClient) : CommunityApi {

    override suspend fun members(channelId: String): ApiResult<List<CommunityMember>> {
        // PaginatedResponse is a flat `{ data: [...] }` (not the single-value StatusResponseDto envelope), so it
        // is read with getDirect (whole-body deserialize) exactly like the channel list. First page only here.
        return when (
            val page: ApiResult<PaginatedEnvelope<CommunityMember>> =
                client.getDirect(
                    "api/v1/channels/$channelId/community?page=1&pageSize=25"
                )
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }
    }
}

/**
 * A community member (backend `CommunityUserDto`): the viewer's identity plus the standing badges the row
 * shows. The field names are the serialized (camelCase) names of `CommunityUserDto`; the client deliberately
 * reads a subset (ApiClient's Json ignores unknown keys), so the heavier stats fields are omitted here.
 */
@Serializable
data class CommunityMember(
    val id: String,
    val username: String = "",
    val displayName: String = "",
    val profileImageUrl: String? = null,
    val trustLevel: String = "viewer",
    val isBanned: Boolean = false,
)

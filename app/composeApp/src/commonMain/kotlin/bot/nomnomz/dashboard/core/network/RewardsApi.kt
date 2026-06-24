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

// The typed rewards facade — the channel's real Twitch channel-point rewards, sourced by the backend from the
// Helix Custom Rewards endpoint (no fabricated rewards). The list is a `PaginatedResponse<RewardListItem>` (a
// flat `{ data: [...] }`), so it is read with getDirect like the channel/community lists. State holders depend
// on this interface and fake it in tests without HTTP.
//
// Backend route (RewardsController):
//   GET /api/v1/channels/{channelId}/rewards  →  PaginatedResponse<RewardListItem>
interface RewardsApi {
    /** The channel's channel-point rewards — the first page the backend resolves. */
    suspend fun list(channelId: String): ApiResult<List<RewardSummary>>
}

class RestRewardsApi(private val client: ApiClient) : RewardsApi {

    override suspend fun list(channelId: String): ApiResult<List<RewardSummary>> {
        // PaginatedResponse is a flat `{ data: [...] }` (not the single-value StatusResponseDto envelope), so it
        // is read with getDirect (whole-body deserialize) exactly like the channel/community lists. First page
        // only here; the pager layers on later.
        return when (
            val page: ApiResult<PaginatedEnvelope<RewardSummary>> =
                client.getDirect("api/v1/channels/$channelId/rewards?page=1&pageSize=25")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }
    }
}

/**
 * A channel-point reward (backend `RewardListItem` — the list-row projection of a reward). The field names are
 * the serialized (camelCase) names of `RewardListItem`; the client reads the subset the row renders (ApiClient's
 * Json ignores unknown keys), so the heavier detail-only fields (prompt, cooldowns, paused) are omitted here.
 */
@Serializable
data class RewardSummary(
    val id: String,
    val title: String = "",
    val cost: Int = 0,
    val isEnabled: Boolean = false,
    val backgroundColor: String? = null,
    val imageUrl: String? = null,
)

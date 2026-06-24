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
// flat `{ data: [...] }`), so it is read with getDirect like the channel/community lists. The same facade also
// drives the page's writes — create / update / toggle / delete — each treated as a Unit result because the
// Rewards page re-lists after every successful write. State holders depend on this interface and fake it in
// tests without HTTP.
//
// Backend routes (RewardsController):
//   GET    /api/v1/channels/{channelId}/rewards            →  PaginatedResponse<RewardListItem>
//   POST   /api/v1/channels/{channelId}/rewards            →  StatusResponseDto<RewardDetail> (201)
//   PUT    /api/v1/channels/{channelId}/rewards/{rewardId} →  StatusResponseDto<RewardDetail>
//   DELETE /api/v1/channels/{channelId}/rewards/{rewardId} →  204 No Content
interface RewardsApi {
    /** The channel's channel-point rewards — the first page the backend resolves. */
    suspend fun list(channelId: String): ApiResult<List<RewardSummary>>

    /** Create a new channel-point reward on the channel (backend POST). */
    suspend fun create(channelId: String, body: CreateRewardBody): ApiResult<Unit>

    /**
     * Update an existing reward, addressed by its [rewardId] (the backend PUT route is keyed by the Twitch
     * reward id). A partial update: only the non-null [body] fields are applied — this is how a toggle is
     * expressed (flip `isEnabled`, leave the rest null).
     */
    suspend fun update(channelId: String, rewardId: String, body: UpdateRewardBody): ApiResult<Unit>

    /** Delete a reward, addressed by its [rewardId] (the backend DELETE route is keyed by the reward id). */
    suspend fun delete(channelId: String, rewardId: String): ApiResult<Unit>
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

    // The create response is a `StatusResponseDto<RewardDetail>` (201), but the controller re-fetches the list
    // after every write, so the body is irrelevant here — any 2xx is success.
    override suspend fun create(channelId: String, body: CreateRewardBody): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/rewards", body)

    override suspend fun update(
        channelId: String,
        rewardId: String,
        body: UpdateRewardBody,
    ): ApiResult<Unit> = client.putUnit("api/v1/channels/$channelId/rewards/$rewardId", body)

    override suspend fun delete(channelId: String, rewardId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/rewards/$rewardId")
}

/**
 * The create-reward request body (backend `CreateRewardRequest`). camelCase JSON. [title] and [cost] are the
 * required essentials the create dialog collects; [prompt] is the optional viewer-facing text. The backend's
 * create DTO has no enabled flag — a freshly created Twitch reward is live by default — so enabling/disabling
 * is an update concern, not a create one.
 */
@Serializable
data class CreateRewardBody(
    val title: String,
    val cost: Int,
    val prompt: String? = null,
)

/**
 * The update-reward request body (backend `UpdateRewardRequest`) — every field nullable so an update is a
 * partial patch. A toggle sends only [isEnabled]; an edit sends [title] / [cost] / [prompt] (and may flip
 * [isEnabled]); all other fields stay null and the backend leaves them untouched. `explicitNulls = false` on
 * the shared Json means null fields are omitted from the wire body.
 */
@Serializable
data class UpdateRewardBody(
    val title: String? = null,
    val cost: Int? = null,
    val prompt: String? = null,
    val isEnabled: Boolean? = null,
)

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

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

// The typed Media-Share facade — the moderator's clip queue (viewers submit Twitch clips / YouTube links via
// the `!media <url>` chat command or a channel-point redeem; a mod approves, reorders, skips, and marks played)
// plus the channel's Media-Share config. Real data only: the backend sources the queue from the actual
// submissions store, never a fabricated request.
//
// The routes are NOT channel-scoped in the path — the active channel rides in the X-Channel-Id header that
// ApiClient attaches to every request, so a channel switch retargets the queue without threading {channelId}
// through the URL. The queue list is a flat `PaginatedResponse<MediaShareRequestDto>` (`{ data: [...] }`) read
// with getDirect; the single-item mutations and the config return a `StatusResponseDto<T>` envelope. The state
// holder depends on this interface and fakes it in tests without HTTP.
//
// Backend routes (MediaShareController):
//   GET  /api/v1/media-share/queue          →  PaginatedResponse<MediaShareRequestDto>
//   GET  /api/v1/media-share/next           →  StatusResponseDto<MediaShareRequestDto>   (overlay only)
//   POST /api/v1/media-share/{id}/approve   →  StatusResponseDto<MediaShareRequestDto>
//   POST /api/v1/media-share/{id}/reject    →  StatusResponseDto<MediaShareRequestDto>
//   POST /api/v1/media-share/{id}/skip      →  StatusResponseDto<MediaShareRequestDto>
//   POST /api/v1/media-share/{id}/played    →  StatusResponseDto<MediaShareRequestDto>
//   POST /api/v1/media-share/{id}/reorder   →  StatusResponseDto<MediaShareRequestDto>
//   GET  /api/v1/media-share/config         →  StatusResponseDto<MediaShareConfigDto>
//   PUT  /api/v1/media-share/config         →  StatusResponseDto<MediaShareConfigDto>
interface MediaShareApi {
    /**
     * The moderator clip queue, newest-first. Pass [status] = "pending" / "approved" / "played" to filter the
     * lane, or null for the whole queue. Flat `PaginatedResponse`.
     */
    suspend fun queue(status: String? = null): ApiResult<List<MediaShareRequest>>

    /**
     * The single next-up clip the OVERLAY plays (backend pops the head of the approved lane). Included for
     * completeness — this is the OBS browser-source's endpoint; the moderator queue page does NOT call it.
     */
    suspend fun next(): ApiResult<MediaShareRequest>

    /** Approve a pending clip so it enters the playable lane. Returns the updated request. */
    suspend fun approve(id: String): ApiResult<MediaShareRequest>

    /** Reject a clip (it leaves the queue). Returns the updated request. */
    suspend fun reject(id: String): ApiResult<MediaShareRequest>

    /** Skip a clip (the mod passes on it without playing). Returns the updated request. */
    suspend fun skip(id: String): ApiResult<MediaShareRequest>

    /** Mark a clip played (it moves out of the active lane once it has aired). Returns the updated request. */
    suspend fun played(id: String): ApiResult<MediaShareRequest>

    /** Move a clip to [position] in the queue (0-based). Returns the updated request. */
    suspend fun reorder(id: String, position: Int): ApiResult<MediaShareRequest>

    /** The channel's Media-Share config (enable flag, source allow-list, limits). */
    suspend fun config(): ApiResult<MediaShareConfig>

    /** Update the channel's Media-Share config. Returns the persisted config. */
    suspend fun updateConfig(body: UpdateMediaShareConfigBody): ApiResult<MediaShareConfig>
}

class RestMediaShareApi(private val client: ApiClient) : MediaShareApi {

    override suspend fun queue(status: String?): ApiResult<List<MediaShareRequest>> {
        // Flat PaginatedResponse (`{ data: [...] }`) read with getDirect. The optional status filters the lane;
        // it is appended only when non-blank, like the rewards redemption filter.
        val statusQuery: String = if (status.isNullOrBlank()) "" else "?status=$status"
        return when (
            val page: ApiResult<PaginatedEnvelope<MediaShareRequest>> =
                client.getDirect("api/v1/media-share/queue$statusQuery")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }
    }

    override suspend fun next(): ApiResult<MediaShareRequest> =
        client.getEnvelope("api/v1/media-share/next")

    override suspend fun approve(id: String): ApiResult<MediaShareRequest> =
        client.postEnvelope("api/v1/media-share/$id/approve")

    override suspend fun reject(id: String): ApiResult<MediaShareRequest> =
        client.postEnvelope("api/v1/media-share/$id/reject")

    override suspend fun skip(id: String): ApiResult<MediaShareRequest> =
        client.postEnvelope("api/v1/media-share/$id/skip")

    override suspend fun played(id: String): ApiResult<MediaShareRequest> =
        client.postEnvelope("api/v1/media-share/$id/played")

    override suspend fun reorder(id: String, position: Int): ApiResult<MediaShareRequest> =
        client.postEnvelope("api/v1/media-share/$id/reorder", ReorderMediaBody(position = position))

    override suspend fun config(): ApiResult<MediaShareConfig> =
        client.getEnvelope("api/v1/media-share/config")

    override suspend fun updateConfig(body: UpdateMediaShareConfigBody): ApiResult<MediaShareConfig> =
        client.putEnvelope("api/v1/media-share/config", body)
}

/**
 * One queued clip (backend `MediaShareRequestDto`). camelCase serialized names; the client reads the subset the
 * queue renders. [sourceType] is `twitch_clip` / `youtube`; [status] is `pending` / `approved` / `playing` /
 * `played` / `rejected` / `skipped`. [queuePosition] is the 0-based lane index (null once played/rejected).
 */
@Serializable
data class MediaShareRequest(
    val id: String = "",
    val requesterUserId: String = "",
    val sourceType: String = "",
    val sourceUrl: String = "",
    val mediaRef: String = "",
    val title: String? = null,
    val durationSeconds: Int = 0,
    val thumbnailUrl: String? = null,
    val status: String = "",
    val queuePosition: Int? = null,
    val requestedAt: String = "",
)

/**
 * The channel's Media-Share config (backend `MediaShareConfigDto`). [entryCost] is the optional currency cost to
 * submit (null = free); the numeric limits are 0 when unbounded.
 */
@Serializable
data class MediaShareConfig(
    val isEnabled: Boolean = false,
    val requireApproval: Boolean = true,
    val allowTwitchClips: Boolean = true,
    val allowYouTube: Boolean = true,
    val maxDurationSeconds: Int = 0,
    val entryCost: Long? = null,
    val maxQueueLength: Int = 0,
    val perUserCooldownSeconds: Int = 0,
)

/** The update-config request body (backend `UpdateMediaShareConfigRequest`) — a full replace of the config. */
@Serializable
data class UpdateMediaShareConfigBody(
    val isEnabled: Boolean,
    val requireApproval: Boolean,
    val allowTwitchClips: Boolean,
    val allowYouTube: Boolean,
    val maxDurationSeconds: Int,
    val entryCost: Long?,
    val maxQueueLength: Int,
    val perUserCooldownSeconds: Int,
)

/** The reorder request body (backend `ReorderMediaRequest`): the target 0-based queue [position]. */
@Serializable
data class ReorderMediaBody(val position: Int)

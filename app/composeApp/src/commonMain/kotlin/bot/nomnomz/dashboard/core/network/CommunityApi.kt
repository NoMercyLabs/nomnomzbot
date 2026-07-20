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
// backend (no fabricated viewer lists). It lists the members and lets a moderator manage each one: set their
// trust level, ban them, or lift a ban. State holders depend on this interface and fake it in tests without
// HTTP.
//
// Backend routes (CommunityController / RewardsController):
//   GET    /api/v1/channels/{channelId}/community                    →  PaginatedResponse<CommunityUserDto>
//   GET    /api/v1/channels/{channelId}/rewards/leaderboard          →  StatusResponseDto<List<LeaderboardEntryDto>>
//   PUT    /api/v1/channels/{channelId}/community/{userId}/trust      →  StatusResponseDto<UserDetailDto>
//   POST   /api/v1/channels/{channelId}/community/{userId}/ban        →  204 No Content
//   DELETE /api/v1/channels/{channelId}/community/{userId}/ban        →  204 No Content
// The list is a `PaginatedResponse<CommunityUserDto>` (a flat `{ data: [...] }`), so it is read with getDirect
// like the channel list. The writes treat any 2xx as success (the trust PUT returns the refreshed user, which
// the controller re-derives by reloading the list — so it is read through putUnit), and `userId` is the Twitch
// id carried by CommunityMember.id.
interface CommunityApi {
    /** The channel's community — the first page of viewers (chatters + mods) the backend resolves. */
    suspend fun members(channelId: String): ApiResult<List<CommunityMember>>

    /**
     * One page of the channel's community, filtered by [role] (`null`/"all", "follower", "vip", "moderator") and
     * paginated (`GET /community?page=&take=&role=&cursor=`). The followers tab is cursor-paginated straight from
     * Twitch — pass the previous page's [CommunityPage.nextCursor] as [cursor] — while the other tabs are
     * page-numbered ([page]). The envelope carries both continuations so the screen can drive next/prev on any tab.
     */
    suspend fun membersPage(
        channelId: String,
        role: String?,
        page: Int,
        pageSize: Int,
        cursor: String?,
    ): ApiResult<CommunityPage>

    /**
     * Autocomplete over the channel's known viewers by name (`GET /community/search?q=&limit=`, backend
     * `SearchViewers`). Each option's [ViewerOption.id] is the Twitch user id the moderation / trust / VIP / ban
     * writes consume — the id the "pick a viewer" picker feeds those actions. Powers reaching a viewer beyond the
     * current page.
     */
    suspend fun searchViewers(
        channelId: String,
        query: String,
        limit: Int = 20,
    ): ApiResult<List<ViewerOption>>

    /**
     * Top chatters by message volume — up to 50 rows ranked by message count (backend `RewardsController
     * .GetLeaderboard`, `GET /rewards/leaderboard`). The endpoint lives in `RewardsController` for historical
     * reasons but is community analytics: it surfaces who is most active in chat.
     */
    suspend fun topChatters(channelId: String): ApiResult<List<ChatActivityEntry>>

    /** Set [userId]'s trust [level] (one of [CommunityTrustLevel]). Non-destructive; takes effect directly. */
    suspend fun setTrust(channelId: String, userId: String, level: String): ApiResult<Unit>

    /** Ban [userId] with [reason]; the backend also enforces it on Twitch. */
    suspend fun ban(channelId: String, userId: String, reason: String): ApiResult<Unit>

    /** Lift the ban on [userId]; the backend also clears it on Twitch. */
    suspend fun unban(channelId: String, userId: String): ApiResult<Unit>

    /** Grant VIP status to [userId] on Twitch. Requires the channel's `channel:manage:vips` scope. */
    suspend fun addVip(channelId: String, userId: String): ApiResult<Unit>

    /** Revoke VIP status from [userId] on Twitch. Requires the channel's `channel:manage:vips` scope. */
    suspend fun removeVip(channelId: String, userId: String): ApiResult<Unit>

    /** Send a /shoutout to [targetTwitchUserId] in the channel. Requires `moderator:manage:shoutouts`. */
    suspend fun shoutout(channelId: String, targetTwitchUserId: String): ApiResult<Unit>
}

class RestCommunityApi(private val client: ApiClient) : CommunityApi {

    override suspend fun members(channelId: String): ApiResult<List<CommunityMember>> {
        // Walk every page so the whole community list shows — flat `{ data, hasMore, nextPage }`.
        return client.getAllPages { page -> "api/v1/channels/$channelId/community?page=$page&pageSize=100" }
    }

    override suspend fun membersPage(
        channelId: String,
        role: String?,
        page: Int,
        pageSize: Int,
        cursor: String?,
    ): ApiResult<CommunityPage> {
        val roleParam: String =
            if (role.isNullOrBlank() || role == "all") "" else "&role=${role.encodeQuery()}"
        val cursorParam: String = if (cursor.isNullOrBlank()) "" else "&cursor=${cursor.encodeQuery()}"
        return client.getDirect(
            "api/v1/channels/$channelId/community?page=$page&take=$pageSize$roleParam$cursorParam"
        )
    }

    override suspend fun searchViewers(
        channelId: String,
        query: String,
        limit: Int,
    ): ApiResult<List<ViewerOption>> =
        client.getEnvelope(
            "api/v1/channels/$channelId/community/search?q=${query.encodeQuery()}&limit=$limit"
        )

    override suspend fun topChatters(channelId: String): ApiResult<List<ChatActivityEntry>> =
        client.getEnvelope("api/v1/channels/$channelId/rewards/leaderboard")

    override suspend fun setTrust(channelId: String, userId: String, level: String): ApiResult<Unit> =
        // The endpoint returns the refreshed UserDetailDto, but the page re-derives its state by reloading the
        // list — so the body is ignored and the write goes through putUnit (any 2xx is success).
        client.putUnit(
            "api/v1/channels/$channelId/community/$userId/trust",
            SetTrustLevelBody(level),
        )

    override suspend fun ban(channelId: String, userId: String, reason: String): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/community/$userId/ban",
            BanBody(reason),
        )

    override suspend fun unban(channelId: String, userId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/community/$userId/ban")

    override suspend fun addVip(channelId: String, userId: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/community/$userId/vip")

    override suspend fun removeVip(channelId: String, userId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/community/$userId/vip")

    override suspend fun shoutout(channelId: String, targetTwitchUserId: String): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/moderation/shoutout",
            ShoutoutBody(targetTwitchUserId),
        )
}

/** The trust levels the backend `SetTrustLevelRequest` accepts — the closed set the row's picker offers. */
object CommunityTrustLevel {
    const val Viewer: String = "viewer"
    const val Subscriber: String = "subscriber"
    const val Vip: String = "vip"
    const val Moderator: String = "moderator"

    /** Ordered for the picker — least to most privileged. */
    val all: List<String> = listOf(Viewer, Subscriber, Vip, Moderator)
}

/** Request body for the trust write (backend `SetTrustLevelRequest`): the new trust level. */
@Serializable
data class SetTrustLevelBody(val level: String)

/** Request body for the ban write (backend `BanRequest`): the moderator-supplied reason. */
@Serializable
data class BanBody(val reason: String)

/** Request body for the shoutout action (backend `ModerationController.ShoutoutRequest`). */
@Serializable
data class ShoutoutBody(val targetTwitchUserId: String)

/**
 * A community member (backend `CommunityUserDto`): the viewer's identity plus the standing badges the row
 * shows. The field names are the serialized (camelCase) names of `CommunityUserDto`; the client deliberately
 * reads it (ApiClient's Json ignores unknown keys), including the per-viewer activity stats surfaced in rows.
 */
@Serializable
data class CommunityMember(
    val id: String,
    /**
     * The viewer's internal platform-identity ULID (backend `internalUserId`), nullable when the backend has no
     * resolved user row yet. This — not the Twitch [id] — is what addresses the channel-scoped analytics profile
     * (`analytics/viewers/{internalUserId}`), so a moderator can read ANY viewer's stats, not only their own.
     */
    val internalUserId: String? = null,
    val username: String = "",
    val displayName: String = "",
    val profileImageUrl: String? = null,
    val trustLevel: String = "viewer",
    val isBanned: Boolean = false,
    /** Per-viewer channel-scoped activity (backend CommunityUserDto): messages sent, watch hours, commands run. */
    val messageCount: Int = 0,
    val watchHours: Double = 0.0,
    val commandsUsed: Int = 0,
    /** ISO-8601 first-/last-seen timestamps (backend DateTime). */
    val firstSeen: String = "",
    val lastSeen: String = "",
)

/**
 * One page of the community list (backend `PaginatedResponse<CommunityUserDto>`). [data] is the page's rows;
 * [nextPage] is the next 1-based page number for the page-numbered tabs (null at the end), [nextCursor] is the
 * Twitch continuation token for the cursor-paginated followers tab (null at the end), and [hasMore] tells the
 * screen whether a "next" affordance should be live. [total] is the full count where the backend knows it.
 */
@Serializable
data class CommunityPage(
    val data: List<CommunityMember> = emptyList(),
    val nextPage: Int? = null,
    val nextCursor: String? = null,
    val hasMore: Boolean = false,
    val total: Int? = null,
)

/**
 * One viewer option for the community picker (backend `CommunityController.ViewerOptionDto`). [id] is the Twitch
 * user id the moderation / trust / VIP / ban writes consume; [label] is the display name and [subLabel] the
 * username.
 */
@Serializable
data class ViewerOption(
    val id: String = "",
    val label: String = "",
    val subLabel: String = "",
)

/**
 * A chat-activity leaderboard row (backend `LeaderboardEntryDto` from `RewardsController.GetLeaderboard`):
 * [rank] (1-based), [userId] (Twitch user id), [displayName], and [points] which is the viewer's all-time
 * message count (the field is named "points" on the wire for historical reasons).
 */
@Serializable
data class ChatActivityEntry(
    val rank: Int = 0,
    val userId: String = "",
    val displayName: String = "",
    val points: Int = 0,
)

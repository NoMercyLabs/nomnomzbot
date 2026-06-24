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
// Backend routes (CommunityController):
//   GET    /api/v1/channels/{channelId}/community            →  PaginatedResponse<CommunityUserDto>
//   PUT    /api/v1/channels/{channelId}/community/{userId}/trust  (SetTrustLevelRequest)  →  StatusResponseDto<UserDetailDto>
//   POST   /api/v1/channels/{channelId}/community/{userId}/ban    (BanRequest)            →  204 No Content
//   DELETE /api/v1/channels/{channelId}/community/{userId}/ban                            →  204 No Content
// The list is a `PaginatedResponse<CommunityUserDto>` (a flat `{ data: [...] }`), so it is read with getDirect
// like the channel list. The writes treat any 2xx as success (the trust PUT returns the refreshed user, which
// the controller re-derives by reloading the list — so it is read through putUnit), and `userId` is the Twitch
// id carried by CommunityMember.id.
interface CommunityApi {
    /** The channel's community — the first page of viewers (chatters + mods) the backend resolves. */
    suspend fun members(channelId: String): ApiResult<List<CommunityMember>>

    /** Set [userId]'s trust [level] (one of [CommunityTrustLevel]). Non-destructive; takes effect directly. */
    suspend fun setTrust(channelId: String, userId: String, level: String): ApiResult<Unit>

    /** Ban [userId] with [reason]; the backend also enforces it on Twitch. */
    suspend fun ban(channelId: String, userId: String, reason: String): ApiResult<Unit>

    /** Lift the ban on [userId]; the backend also clears it on Twitch. */
    suspend fun unban(channelId: String, userId: String): ApiResult<Unit>
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

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

// Backend routes (UsersController):
//   GET    /api/v1/users?query=…&page=&pageSize=      → PaginatedResponse<UserDto>              (viewer search)
//   GET    /api/v1/users/{userId}/stats            → StatusResponseDto<UserStatsDto>
//   POST   /api/v1/users/{userId}/export           → StatusResponseDto<Unit> (triggers data export email)
//   DELETE /api/v1/users/{userId}                  → 204 No Content          (GDPR erasure, Broadcaster-only)
interface UsersApi {
    /**
     * Search the platform's users by login/display name (`GET /users?query=…`) — the internal-GUID viewer picker.
     * Each result's [UserSearchResult.id] is the platform User GUID that the economy writes (transfer / adjust,
     * keyed on `viewerUserId:guid`) consume. This is the same idiom the Roles page uses; the two share the
     * `UserSearchResult` DTO declared in RolesApi.kt.
     */
    suspend fun search(query: String, limit: Int = 20): ApiResult<List<UserSearchResult>>

    suspend fun stats(userId: String): ApiResult<UserStats>
    suspend fun export(userId: String): ApiResult<Unit>
    suspend fun erase(userId: String): ApiResult<Unit>
}

class RestUsersApi(private val client: ApiClient) : UsersApi {
    override suspend fun search(query: String, limit: Int): ApiResult<List<UserSearchResult>> =
        // A flat `{ data: [...] }` PaginatedResponse (like the channel list), so getDirect + unwrap. The tenant the
        // search authorizes against comes from the ApiClient's X-Channel-Id (the operator's active channel).
        when (
            val page: ApiResult<PaginatedEnvelope<UserSearchResult>> =
                client.getDirect("api/v1/users?query=${query.encodeQuery()}&page=1&pageSize=$limit")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }

    override suspend fun stats(userId: String): ApiResult<UserStats> =
        client.getEnvelope("api/v1/users/$userId/stats")

    override suspend fun export(userId: String): ApiResult<Unit> =
        client.postUnit("api/v1/users/$userId/export")

    override suspend fun erase(userId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/users/$userId")
}

@kotlinx.serialization.Serializable
data class UserStats(
    val messageCount: Int = 0,
    val watchHours: Double = 0.0,
    val channelsCount: Int = 0,
    val commandsUsed: Int = 0,
    val firstSeen: String? = null,
    val lastActive: String? = null,
    val exportAvailable: Boolean = true,
)

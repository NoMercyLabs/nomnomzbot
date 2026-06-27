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
//   GET    /api/v1/users/{userId}/stats            → StatusResponseDto<UserStatsDto>
//   POST   /api/v1/users/{userId}/export           → StatusResponseDto<Unit> (triggers data export email)
//   DELETE /api/v1/users/{userId}                  → 204 No Content          (GDPR erasure, Broadcaster-only)
interface UsersApi {
    suspend fun stats(userId: String): ApiResult<UserStats>
    suspend fun export(userId: String): ApiResult<Unit>
    suspend fun erase(userId: String): ApiResult<Unit>
}

class RestUsersApi(private val client: ApiClient) : UsersApi {
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

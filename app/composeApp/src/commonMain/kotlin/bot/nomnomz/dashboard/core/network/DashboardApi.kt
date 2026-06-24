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

// The typed dashboard facade — the live channel snapshot the Home page renders. Real data only: the backend
// aggregates it from the live Twitch/EventSub state (no fabricated counts). State holders depend on this
// interface and fake it in tests without HTTP.
//
// Backend route (DashboardController):
//   GET /api/v1/dashboard/{channelId}/stats  →  StatusResponseDto<DashboardStatsDto>
interface DashboardApi {
    /** The channel's current snapshot — live state, stream info, and the headline counters. */
    suspend fun stats(channelId: String): ApiResult<DashboardStats>
}

class RestDashboardApi(private val client: ApiClient) : DashboardApi {
    override suspend fun stats(channelId: String): ApiResult<DashboardStats> =
        client.getEnvelope("api/v1/dashboard/$channelId/stats")
}

/** The channel snapshot (backend `DashboardStatsDto`): live state, current stream info, and headline counts. */
@Serializable
data class DashboardStats(
    val isLive: Boolean = false,
    val streamTitle: String? = null,
    val gameName: String? = null,
    val viewerCount: Int = 0,
    val followerCount: Int = 0,
    val commandsUsed: Long = 0,
    val messagesCount: Long = 0,
    /** Seconds the current stream has been live, or null when offline. */
    val uptime: Long? = null,
)

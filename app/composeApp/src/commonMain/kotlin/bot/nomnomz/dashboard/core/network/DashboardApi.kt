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
// Backend routes (DashboardController):
//   GET /api/v1/dashboard/{channelId}/stats     → StatusResponseDto<DashboardStatsDto>
//   GET /api/v1/dashboard/{channelId}/activity  → StatusResponseDto<List<ActivityEventDto>>
interface DashboardApi {
    /** The channel's current snapshot — live state, stream info, and the headline counters. */
    suspend fun stats(channelId: String): ApiResult<DashboardStats>

    /** The 20 most recent channel events (follows, subs, cheers, raids, redemptions, etc.) newest-first. */
    suspend fun activity(channelId: String): ApiResult<List<ActivityEvent>>
}

class RestDashboardApi(private val client: ApiClient) : DashboardApi {
    override suspend fun stats(channelId: String): ApiResult<DashboardStats> =
        client.getEnvelope("api/v1/dashboard/$channelId/stats")

    override suspend fun activity(channelId: String): ApiResult<List<ActivityEvent>> =
        client.getEnvelope("api/v1/dashboard/$channelId/activity")
}

/**
 * One recent channel event (backend `ActivityEventDto`): event type, optional chatter, payload, and timestamp.
 * [type] is a backend-defined string (e.g. "follow", "subscribe", "cheer", "raid", "redemption").
 */
@Serializable
data class ActivityEvent(
    val id: String,
    val type: String,
    val userId: String? = null,
    val username: String? = null,
    val data: String? = null,
    val timestamp: String = "",
)

/** The channel snapshot (backend `DashboardStatsDto`): live state, current stream info, and headline counts. */
@Serializable
data class DashboardStats(
    val isLive: Boolean = false,
    val streamTitle: String? = null,
    val gameName: String? = null,
    val viewerCount: Int = 0,
    val followerCount: Int = 0,
    /** Real Twitch subscriber total (Get Broadcaster Subscriptions); 0 when the Helix read fails. */
    val subscriberCount: Int = 0,
    /** Distinct chatters seen today (UTC) — the privacy-hashed count, never fabricated. */
    val chattersToday: Int = 0,
    /** Supporter events (tips/memberships/merch/charity) received today (UTC). */
    val supporterEventsToday: Int = 0,
    /**
     * Today's supporter total in MINOR units (divide by 100 for display), or null on a mixed-currency /
     * amount-less day — render the [supporterEventsToday] count alone then, never a fabricated 0.00.
     */
    val supporterAmountMinorToday: Long? = null,
    /** The single currency behind [supporterAmountMinorToday], else null. */
    val supporterCurrency: String? = null,
    /** Platforms the owner is live on right now (`twitch`/`youtube`/`kick`), alphabetical; empty = offline. */
    val platformsLive: List<String> = emptyList(),
    val commandsUsed: Long = 0,
    val messagesCount: Long = 0,
    /** Seconds the current stream has been live, or null when offline. */
    val uptime: Long? = null,
)

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

// The typed facade over the engagement-trigger config (auto-greet a first-time chatter, shout out a returning
// regular, celebrate a watch-streak milestone). Tenant-resolved routes (no {channelId}) — the active channel
// rides on the X-Channel-Id header ApiClient sends, so nothing is threaded through here.
//
// Backend (EngagementController):
//   GET /api/v1/engagement/config → StatusResponseDto<EngagementConfigDto>
//   PUT /api/v1/engagement/config → StatusResponseDto<EngagementConfigDto>
interface EngagementApi {
    /** The channel's engagement-trigger config (backend defaults — all off — when never set). */
    suspend fun getConfig(): ApiResult<EngagementConfig>

    /** Persist the trigger toggles + milestone list + greet cooldown; echoes the saved config. */
    suspend fun setConfig(body: UpdateEngagementConfigBody): ApiResult<EngagementConfig>
}

class RestEngagementApi(private val client: ApiClient) : EngagementApi {
    override suspend fun getConfig(): ApiResult<EngagementConfig> =
        client.getEnvelope("api/v1/engagement/config")

    override suspend fun setConfig(body: UpdateEngagementConfigBody): ApiResult<EngagementConfig> =
        client.putEnvelope("api/v1/engagement/config", body)
}

/**
 * The per-channel engagement configuration (`EngagementConfigDto`). Three opt-in trigger toggles, the
 * watch-streak [streakMilestones] the bot celebrates (ascending session counts), and [greetCooldownSeconds]
 * — the minimum gap between greeting the same viewer. All default to off/empty (opt-in).
 */
@Serializable
data class EngagementConfig(
    val firstTimeChatterEnabled: Boolean = false,
    val returningChatterEnabled: Boolean = false,
    val watchStreakEnabled: Boolean = false,
    val streakMilestones: List<Int> = emptyList(),
    val greetCooldownSeconds: Int = 0,
)

/** Update body for the engagement config (`UpdateEngagementConfigRequest`); omit [streakMilestones] to keep it. */
@Serializable
data class UpdateEngagementConfigBody(
    val firstTimeChatterEnabled: Boolean,
    val returningChatterEnabled: Boolean,
    val watchStreakEnabled: Boolean,
    val streakMilestones: List<Int>?,
    val greetCooldownSeconds: Int,
)

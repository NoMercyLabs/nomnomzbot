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

// The typed games facade — the channel's configured mini-games, sourced from the backend's real game config
// (economy.md §3.5; no fabricated games). The list is a `StatusResponseDto<List<GameConfigDto>>` (the single-value
// `{ data: <T> }` envelope where T is the list), so it is read with getEnvelope — unlike the paginated lists
// (channels/community) which are flat `PaginatedResponse`. State holders depend on this interface and fake it in
// tests without HTTP.
//
// Backend route (GamesController):
//   GET /api/v1/channels/{channelId}/economy/games  →  StatusResponseDto<List<GameConfigDto>>
interface GamesApi {
    /** The channel's configured mini-games. */
    suspend fun list(channelId: String): ApiResult<List<GameSummary>>
}

class RestGamesApi(private val client: ApiClient) : GamesApi {
    override suspend fun list(channelId: String): ApiResult<List<GameSummary>> =
        client.getEnvelope("api/v1/channels/$channelId/economy/games")
}

/**
 * One configured mini-game (backend `GameConfigDto`): the game's identity plus the config the read-only row
 * shows. The field names are the serialized (camelCase) names of `GameConfigDto`; the client deliberately reads a
 * subset (ApiClient's Json ignores unknown keys), so the odds/payout/`config`-map fields are omitted here.
 */
@Serializable
data class GameSummary(
    val id: String,
    val gameType: String,
    val category: String = "",
    val isEnabled: Boolean = false,
    val requires18Plus: Boolean = false,
    val minBet: Long? = null,
    val maxBet: Long? = null,
    val cooldownSeconds: Int = 0,
)

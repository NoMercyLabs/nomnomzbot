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
import kotlinx.serialization.json.JsonObject

// The typed games facade — the channel's configured mini-games, sourced from the backend's real game config
// (economy.md §3.5; no fabricated games). The list is a `StatusResponseDto<List<GameConfigDto>>` (the single-value
// `{ data: <T> }` envelope where T is the list), so it is read with getEnvelope — unlike the paginated lists
// (channels/community) which are flat `PaginatedResponse`. State holders depend on this interface and fake it in
// tests without HTTP.
//
// Backend routes (GamesController):
//   GET    /api/v1/channels/{channelId}/economy/games          →  StatusResponseDto<List<GameConfigDto>>
//   PUT    /api/v1/channels/{channelId}/economy/games          →  StatusResponseDto<GameConfigDto>
//   GET    /api/v1/channels/{channelId}/economy/games/history  →  PaginatedResponse<GamePlayDto>
//   DELETE /api/v1/channels/{channelId}/economy/games/consent/{viewerUserId}  →  StatusResponseDto
//
// Games are a fixed catalog of built-in game types — the controller exposes no create or delete route. The only
// write is the upsert PUT, addressed by `gameType` in the body (the service keys on `(BroadcasterId, GameType)`),
// so management is TOGGLE enabled + EDIT config, never add/remove. The PUT is a FULL replace (not a patch): the
// service writes every field of the request, so a toggle or a config edit must carry the row's other fields back
// unchanged or they would be reset — the controller builds the request from the current [GameSummary] to do exactly
// that.
interface GamesApi {
    /** The channel's configured mini-games. */
    suspend fun list(channelId: String): ApiResult<List<GameSummary>>

    /** Upsert a game's config (full PUT, addressed by [UpsertGameConfigBody.gameType]). */
    suspend fun upsert(channelId: String, body: UpsertGameConfigBody): ApiResult<Unit>

    /** Paginated game-play history for this channel (Moderator+). Optionally filter by game or player. */
    suspend fun history(channelId: String, page: Int = 1, pageSize: Int = 25): ApiResult<PaginatedEnvelope<GamePlayEntry>>

    /** Revoke a viewer's age-consent grant (Broadcaster/Editor). */
    suspend fun revokeConsent(channelId: String, viewerUserId: String): ApiResult<Unit>
}

class RestGamesApi(private val client: ApiClient) : GamesApi {
    override suspend fun list(channelId: String): ApiResult<List<GameSummary>> =
        client.getEnvelope("api/v1/channels/$channelId/economy/games")

    // The upsert response is a `StatusResponseDto<GameConfigDto>`, but the controller re-fetches the list after
    // every write, so the body is irrelevant here — any 2xx is success.
    override suspend fun upsert(channelId: String, body: UpsertGameConfigBody): ApiResult<Unit> =
        client.putUnit("api/v1/channels/$channelId/economy/games", body)

    override suspend fun history(
        channelId: String,
        page: Int,
        pageSize: Int,
    ): ApiResult<PaginatedEnvelope<GamePlayEntry>> =
        client.getDirect(
            "api/v1/channels/$channelId/economy/games/history?page=$page&pageSize=$pageSize"
        )

    override suspend fun revokeConsent(channelId: String, viewerUserId: String): ApiResult<Unit> =
        client.deleteUnit(
            "api/v1/channels/$channelId/economy/games/consent/$viewerUserId"
        )
}

/**
 * One game-play history row (backend `GamePlayDto`): identity of the play + the settled outcome + amounts.
 * Used on the management history tab (Moderator+); contains no PII beyond the player UUID.
 */
@Serializable
data class GamePlayEntry(
    val id: Long,
    val gameConfigId: String,
    val playerUserId: String,
    val betAmount: Long,
    val outcome: String,
    val payoutAmount: Long,
    val netResult: Long,
    val createdAt: String,
)

/**
 * One configured mini-game (backend `GameConfigDto`): the game's identity plus its full config. The field names
 * are the serialized (camelCase) names of `GameConfigDto`. The whole config is carried (not a subset) because the
 * upsert PUT is a full replace — a toggle/edit echoes the unchanged fields back through [UpsertGameConfigBody], so
 * dropping a field here would silently reset it on the next write. [config] is the game's opaque tuning map,
 * round-tripped verbatim.
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
    val houseEdgePercent: Double? = null,
    val winChancePercent: Double? = null,
    val payoutMultiplier: Double? = null,
    val cooldownSeconds: Int = 0,
    val maxPlaysPerStream: Int? = null,
    val permission: String = "",
    val config: JsonObject? = null,
)

/**
 * The upsert-game request body (backend `UpsertGameConfigRequest`). camelCase JSON. The PUT is a FULL replace
 * (the service writes every field), so this carries the complete config — the editable fields the dialog collects
 * ([isEnabled], [minBet], [maxBet], [cooldownSeconds], [requires18Plus]) plus the fields preserved unchanged from
 * the current row ([gameType] is the address; [category], [permission], the odds, [maxPlaysPerStream], [config]).
 */
@Serializable
data class UpsertGameConfigBody(
    val gameType: String,
    val category: String,
    val isEnabled: Boolean,
    val requires18Plus: Boolean,
    val minBet: Long?,
    val maxBet: Long?,
    val houseEdgePercent: Double?,
    val winChancePercent: Double?,
    val payoutMultiplier: Double?,
    val cooldownSeconds: Int,
    val maxPlaysPerStream: Int?,
    val permission: String,
    val config: JsonObject?,
)

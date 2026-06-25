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

// The typed economy facade — the channel's currency definition (the config the Economy page reads and edits)
// plus the points leaderboard (the top holders, read-only). State holders depend on this interface and fake it
// in tests without HTTP.
//
// Backend routes:
//   CurrencyController:
//     GET /api/v1/channels/{channelId}/economy/config  →  StatusResponseDto<CurrencyConfigDto?>  (null = not configured)
//     PUT /api/v1/channels/{channelId}/economy/config  ←  UpsertCurrencyConfigRequest  →  StatusResponseDto<CurrencyConfigDto>
//   EconomyLeaderboardsController:
//     GET /api/v1/channels/{channelId}/economy/leaderboards/configs        →  StatusResponseDto<List<LeaderboardConfigDto>>
//     GET /api/v1/channels/{channelId}/economy/leaderboards/{configId}?top  →  StatusResponseDto<List<LeaderboardEntryDto>>
//
// The leaderboard is a two-step read: the channel can have several configured leaderboards, so the ranking is
// addressed by a config id. The Economy page surfaces the channel's primary ranking, so [leaderboard] resolves the
// first configured leaderboard, then fetches its live ranking; a channel with no configured leaderboard yields an
// empty ranking (not an error) so the page renders the config form with an empty holders list.
interface EconomyApi {
    /** The channel's currency definition, or null when the economy has never been configured. */
    suspend fun config(channelId: String): ApiResult<CurrencyConfig?>

    /** Persist [update]; the backend echoes the saved configuration back. */
    suspend fun updateConfig(
        channelId: String,
        update: UpsertCurrencyConfig,
    ): ApiResult<CurrencyConfig>

    /** The channel's primary points leaderboard — the top holders, capped at [top] rows. */
    suspend fun leaderboard(channelId: String, top: Int): ApiResult<List<LeaderboardEntry>>

    /** The channel's currency accounts — viewer balances + lifetime totals. First page only here. */
    suspend fun accounts(channelId: String): ApiResult<List<CurrencyAccountSummary>>

    /** The channel's earning rules — how viewers earn currency (per source). The full set, read-only here. */
    suspend fun earningRules(channelId: String): ApiResult<List<EarningRule>>

    /** Freeze or unfreeze a viewer's account ([frozen]) — a frozen account can neither earn nor spend. */
    suspend fun freezeAccount(
        channelId: String,
        viewerUserId: String,
        frozen: Boolean,
    ): ApiResult<Unit>

    /** The channel's store catalog — the items viewers buy with currency. First page only here. */
    suspend fun catalog(channelId: String): ApiResult<List<CatalogItem>>
}

class RestEconomyApi(private val client: ApiClient) : EconomyApi {
    // The config can legitimately be null (the economy was never set up), and getEnvelope treats a null `data`
    // as an EMPTY_BODY failure — so the whole StatusResponse<CurrencyConfig?> is read directly and its `data`
    // unwrapped by hand, preserving null as the valid "not configured yet" state.
    override suspend fun config(channelId: String): ApiResult<CurrencyConfig?> =
        when (
            val result: ApiResult<StatusResponse<CurrencyConfig?>> =
                client.getDirect("api/v1/channels/$channelId/economy/config")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(result.error)
            is ApiResult.Ok -> ApiResult.Ok(result.value.data)
        }

    override suspend fun updateConfig(
        channelId: String,
        update: UpsertCurrencyConfig,
    ): ApiResult<CurrencyConfig> =
        client.putEnvelope("api/v1/channels/$channelId/economy/config", update)

    override suspend fun leaderboard(
        channelId: String,
        top: Int,
    ): ApiResult<List<LeaderboardEntry>> {
        // Resolve the channel's configured leaderboards, then rank the first one. No configured leaderboard is a
        // valid state — the holders list is simply empty — so it is not surfaced as an error.
        val configs: List<LeaderboardConfig> =
            when (
                val result: ApiResult<List<LeaderboardConfig>> =
                    client.getEnvelope(
                        "api/v1/channels/$channelId/economy/leaderboards/configs"
                    )
            ) {
                is ApiResult.Failure -> return ApiResult.Failure(result.error)
                is ApiResult.Ok -> result.value
            }

        val primary: LeaderboardConfig =
            configs.firstOrNull() ?: return ApiResult.Ok(emptyList())

        return client.getEnvelope(
            "api/v1/channels/$channelId/economy/leaderboards/${primary.id}?top=$top"
        )
    }

    // Flat PaginatedResponse like the other lists — read with getDirect. First page only; the pager layers later.
    override suspend fun accounts(channelId: String): ApiResult<List<CurrencyAccountSummary>> =
        when (
            val page: ApiResult<PaginatedEnvelope<CurrencyAccountSummary>> =
                client.getDirect("api/v1/channels/$channelId/economy/accounts?page=1&pageSize=25")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }

    // StatusResponseDto envelope wrapping the rule list (ResultResponse over a Result<list>) — getEnvelope reads
    // the `data` list directly, exactly like the leaderboard configs.
    override suspend fun earningRules(channelId: String): ApiResult<List<EarningRule>> =
        client.getEnvelope("api/v1/channels/$channelId/economy/earning-rules")

    override suspend fun freezeAccount(
        channelId: String,
        viewerUserId: String,
        frozen: Boolean,
    ): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/economy/accounts/$viewerUserId/freeze",
            FreezeAccountBody(frozen),
        )

    override suspend fun catalog(channelId: String): ApiResult<List<CatalogItem>> =
        when (
            val page: ApiResult<PaginatedEnvelope<CatalogItem>> =
                client.getDirect("api/v1/channels/$channelId/economy/catalog?page=1&pageSize=25")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }
}

/** The freeze/unfreeze request body (backend `CurrencyController.FreezeBody`). camelCase `frozen`. */
@Serializable
data class FreezeAccountBody(val frozen: Boolean)

// The store-item DTO `CatalogItem` (backend `CatalogItemDto`) is declared once in ParticipantApi.kt and shared —
// the Economy page's catalog read returns that same type rather than re-declaring it (one DTO per backend shape).

/**
 * The channel's currency definition (backend `CurrencyConfigDto`). Field names mirror the DTO camelCase exactly.
 * The Economy page reads this and edits the operator-controlled settings the upsert accepts ([UpsertCurrencyConfig]).
 */
@Serializable
data class CurrencyConfig(
    val id: String = "",
    val broadcasterId: String = "",
    val currencyName: String = "",
    val currencyNamePlural: String? = null,
    val iconUrl: String? = null,
    val isEnabled: Boolean = false,
    val startingBalance: Long = 0,
    val maxBalance: Long? = null,
    val decimalPlaces: Int = 0,
    val createdAt: String = "",
    val updatedAt: String = "",
)

/**
 * The currency-config upsert request (backend `UpsertCurrencyConfigRequest`). camelCase JSON; this is a full
 * replace (the service writes every field), so the form sends the complete edited config. `currencyNamePlural`
 * and `iconUrl` are optional; with `explicitNulls = false` on the shared Json a null is omitted from the body.
 */
@Serializable
data class UpsertCurrencyConfig(
    val currencyName: String,
    val currencyNamePlural: String? = null,
    val iconUrl: String? = null,
    val isEnabled: Boolean,
    val startingBalance: Long,
    val maxBalance: Long? = null,
    val decimalPlaces: Int,
)

/** One ranked holder in the points leaderboard (backend `LeaderboardEntryDto`). camelCase mirror of the DTO. */
@Serializable
data class LeaderboardEntry(
    val rank: Int = 0,
    val userId: String = "",
    val displayName: String = "",
    val points: Long = 0,
)

/**
 * One configured leaderboard (backend `LeaderboardConfigDto`) — only [id] is used here, to address the ranking
 * read. The full config CRUD is a separate management surface; the Economy page consumes the primary ranking only.
 */
@Serializable
data class LeaderboardConfig(
    val id: String = "",
    val metric: String = "",
    val scope: String = "",
    val period: String = "",
    val isPublic: Boolean = false,
    val topN: Int = 0,
)

/**
 * One viewer's currency account (backend `CurrencyAccountDto`) — the account-admin row. camelCase mirror; the
 * Economy page reads the balance + lifetime totals + frozen flag. [viewerTwitchUserId] identifies the holder
 * (the display name is resolved elsewhere); [lastActivityAt] is the ISO-8601 last-movement time, or null.
 */
@Serializable
data class CurrencyAccountSummary(
    val id: String = "",
    val viewerUserId: String = "",
    val viewerTwitchUserId: String = "",
    val balance: Long = 0,
    val lifetimeEarned: Long = 0,
    val lifetimeSpent: Long = 0,
    val isFrozen: Boolean = false,
    val lastActivityAt: String? = null,
)

/**
 * One earning rule (backend `EarningRuleDto`) — how viewers earn currency from a [source] (e.g. chat_message,
 * watch_time), at [rate] per unit, optionally windowed ([unitWindowSeconds]) and capped ([perWindowCap] /
 * [perStreamCap]) and role-gated ([minRoleLevel]). camelCase mirror; the backend's nested `bonusConfig` map is
 * deliberately omitted (the page reads the scalar rule shape — the contract test allows a subset).
 */
@Serializable
data class EarningRule(
    val id: String = "",
    val source: String = "",
    val isEnabled: Boolean = false,
    val rate: Long = 0,
    val unitWindowSeconds: Int? = null,
    val perWindowCap: Long? = null,
    val perStreamCap: Long? = null,
    val minRoleLevel: Int? = null,
    val configSchemaVersion: Int = 0,
)

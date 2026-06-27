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

    /** Enable or disable a catalog item ([enabled]) — a partial PATCH carrying only the flag. */
    suspend fun setCatalogItemEnabled(
        channelId: String,
        itemId: String,
        enabled: Boolean,
    ): ApiResult<Unit>

    /** Create a new store catalog item and return the saved item. */
    suspend fun createCatalogItem(
        channelId: String,
        request: CreateCatalogItemBody,
    ): ApiResult<CatalogItem>

    /** Delete a catalog item ([itemId]) permanently. */
    suspend fun deleteCatalogItem(channelId: String, itemId: String): ApiResult<Unit>

    /**
     * Upsert an earning rule (full PUT; keyed by [source] in the body). The backend creates or replaces the rule for
     * [source]; used for toggling [isEnabled] or editing the rate and caps.
     */
    suspend fun upsertEarningRule(
        channelId: String,
        request: UpsertEarningRuleBody,
    ): ApiResult<EarningRule>

    /** The channel's community savings jars — open and closed. Full list (first page). */
    suspend fun savingsJars(channelId: String): ApiResult<List<SavingsJar>>

    /** Create a new savings jar and return the saved jar. */
    suspend fun createSavingsJar(
        channelId: String,
        request: CreateSavingsJarBody,
    ): ApiResult<SavingsJar>

    /** Admin-adjust a viewer's balance (positive = credit, negative = debit). */
    suspend fun adjustAccount(
        channelId: String,
        viewerUserId: String,
        amount: Long,
        reason: String?,
    ): ApiResult<Unit>

    /** The full catalog purchase history for the channel — first page. */
    suspend fun catalogPurchases(channelId: String): ApiResult<List<CatalogPurchase>>

    /** Refund a catalog purchase — credits the cost back to the buyer. */
    suspend fun refundPurchase(channelId: String, purchaseId: Long): ApiResult<Unit>

    /** Delete a custom earning rule permanently (built-in sources auto-recreate; this removes custom overrides). */
    suspend fun deleteEarningRule(channelId: String, ruleId: String): ApiResult<Unit>

    /** Transaction ledger for a specific account — first page (newest first). */
    suspend fun ledger(channelId: String, viewerUserId: String): ApiResult<List<CurrencyLedgerEntry>>

    /** Transfer [amount] from one viewer's account to another. Broadcaster/Editor only. */
    suspend fun transfer(channelId: String, request: TransferBody): ApiResult<Unit>
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

    override suspend fun setCatalogItemEnabled(
        channelId: String,
        itemId: String,
        enabled: Boolean,
    ): ApiResult<Unit> =
        client.patchUnit(
            "api/v1/channels/$channelId/economy/catalog/$itemId",
            UpdateCatalogItemBody(isEnabled = enabled),
        )

    override suspend fun createCatalogItem(
        channelId: String,
        request: CreateCatalogItemBody,
    ): ApiResult<CatalogItem> =
        client.postEnvelope("api/v1/channels/$channelId/economy/catalog", request)

    override suspend fun deleteCatalogItem(channelId: String, itemId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/economy/catalog/$itemId")

    // The earning-rules PUT is a full upsert keyed by source in the body (no ruleId in the URL).
    override suspend fun upsertEarningRule(
        channelId: String,
        request: UpsertEarningRuleBody,
    ): ApiResult<EarningRule> =
        client.putEnvelope("api/v1/channels/$channelId/economy/earning-rules", request)

    override suspend fun savingsJars(channelId: String): ApiResult<List<SavingsJar>> =
        client.getEnvelope("api/v1/channels/$channelId/economy/jars")

    override suspend fun createSavingsJar(
        channelId: String,
        request: CreateSavingsJarBody,
    ): ApiResult<SavingsJar> =
        client.postEnvelope("api/v1/channels/$channelId/economy/jars", request)

    override suspend fun adjustAccount(
        channelId: String,
        viewerUserId: String,
        amount: Long,
        reason: String?,
    ): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/economy/accounts/$viewerUserId/adjust",
            AdminAdjustBody(amount, reason),
        )

    override suspend fun catalogPurchases(channelId: String): ApiResult<List<CatalogPurchase>> =
        when (val page: ApiResult<PaginatedEnvelope<CatalogPurchase>> = client.getDirect("api/v1/channels/$channelId/economy/catalog/purchases?page=1&pageSize=50")) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }

    override suspend fun refundPurchase(channelId: String, purchaseId: Long): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/economy/catalog/purchases/$purchaseId/refund", Unit)

    override suspend fun deleteEarningRule(channelId: String, ruleId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/economy/earning-rules/$ruleId")

    override suspend fun ledger(
        channelId: String,
        viewerUserId: String,
    ): ApiResult<List<CurrencyLedgerEntry>> =
        when (
            val page: ApiResult<PaginatedEnvelope<CurrencyLedgerEntry>> =
                client.getDirect(
                    "api/v1/channels/$channelId/economy/accounts/$viewerUserId/ledger?page=1&pageSize=50"
                )
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }

    override suspend fun transfer(channelId: String, request: TransferBody): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/economy/transfer", request)
}

/** The freeze/unfreeze request body (backend `CurrencyController.FreezeBody`). camelCase `frozen`. */
@Serializable
data class FreezeAccountBody(val frozen: Boolean)

/** Admin balance adjustment (backend `AdminAdjustCommand`). Positive = credit, negative = debit. */
@Serializable
data class AdminAdjustBody(val amount: Long, val reason: String? = null)

/** One catalog purchase (backend `CatalogPurchaseDto`). */
@Serializable
data class CatalogPurchase(
    val id: Long = 0,
    val catalogItemId: String = "",
    val buyerUserId: String = "",
    val costPaid: Long = 0,
    val itemNameSnapshot: String = "",
    val status: String = "",
    val createdAt: String = "",
)

/**
 * A partial catalog-item update (backend `UpdateCatalogItemRequest`) — every field nullable, only the non-null
 * ones apply. A toggle sends just [isEnabled]; `explicitNulls = false` on the shared Json omits the rest.
 */
@Serializable
data class UpdateCatalogItemBody(val isEnabled: Boolean? = null)

/**
 * A new catalog-item request (backend `CreateCatalogItemRequest`). Required: [name], [sinkType], [cost];
 * everything else has a sensible default. [permission] must be a valid community-standing value ("Everyone" etc.).
 */
@Serializable
data class CreateCatalogItemBody(
    val name: String,
    val description: String? = null,
    val sinkType: String = "currency",
    val cost: Long,
    val iconUrl: String? = null,
    val isEnabled: Boolean = true,
    val permission: String = "Everyone",
    val pipelineId: String? = null,
    val cooldownSeconds: Int = 0,
    val cooldownPerUser: Boolean = false,
    val stockLimit: Int? = null,
    val maxPerViewerPerStream: Int? = null,
    val sortOrder: Int? = null,
)

/**
 * A full earning-rule upsert (backend `UpsertEarningRuleRequest`). Keyed by [source]; the backend creates or
 * replaces the rule for that source. [bonusConfig] is deliberately omitted (the dashboard doesn't surface it yet).
 */
@Serializable
data class UpsertEarningRuleBody(
    val source: String,
    val isEnabled: Boolean,
    val rate: Long,
    val unitWindowSeconds: Int? = null,
    val perWindowCap: Long? = null,
    val perStreamCap: Long? = null,
    val minRoleLevel: Int? = null,
)

/**
 * A new savings jar request (backend `CreateSavingsJarRequest`). Required: [name], [isOpen]; goal/icon/cap optional.
 */
@Serializable
data class CreateSavingsJarBody(
    val name: String,
    val description: String? = null,
    val goalAmount: Long? = null,
    val iconUrl: String? = null,
    val isOpen: Boolean = true,
    val maxWithdrawalPerChannel: Long? = null,
)

// The store-item DTO `CatalogItem` and `SavingsJar` (backend `CatalogItemDto` / `SavingsJarDto`) are declared once
// in ParticipantApi.kt and shared — the Economy page reuses those types rather than re-declaring them.

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

/**
 * One immutable ledger movement (backend `CurrencyLedgerEntryDto`). camelCase mirror; [entryType] / [sourceType]
 * are opaque tokens — the UI displays them as-is. [amount] is signed (positive = credit, negative = debit).
 */
@Serializable
data class CurrencyLedgerEntry(
    val id: Long = 0,
    val amount: Long = 0,
    val balanceAfter: Long = 0,
    val entryType: String = "",
    val sourceType: String? = null,
    val reason: String? = null,
    val createdAt: String = "",
)

// TransferBody is declared in ParticipantApi.kt (same package) and shared here.

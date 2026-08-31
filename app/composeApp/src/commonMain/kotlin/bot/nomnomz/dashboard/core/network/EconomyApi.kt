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

    /** The channel's configured leaderboards (metric/scope/period/visibility/size) — the management list. */
    suspend fun leaderboardConfigs(channelId: String): ApiResult<List<LeaderboardConfig>>

    /** Create or update a leaderboard config ([request.id] null = create, set = update-by-id). */
    suspend fun upsertLeaderboardConfig(
        channelId: String,
        request: UpsertLeaderboardConfigBody,
    ): ApiResult<LeaderboardConfig>

    /** Delete a leaderboard config permanently. */
    suspend fun deleteLeaderboardConfig(channelId: String, configId: String): ApiResult<Unit>

    /**
     * The real, backend-counted blast radius of deleting this leaderboard config (S-CONSEQ). The confirm dialog
     * MUST call this and render the result before the destructive save can proceed.
     */
    suspend fun leaderboardConfigBlastRadius(
        channelId: String,
        configId: String,
    ): ApiResult<BlastRadiusSummary>

    /** Opt [viewerUserId] out of the channel's leaderboards — hidden from every ranking until opted back in. */
    suspend fun optOutOfLeaderboards(channelId: String, viewerUserId: String): ApiResult<Unit>

    /** Opt [viewerUserId] back into the channel's leaderboards. */
    suspend fun optInToLeaderboards(channelId: String, viewerUserId: String): ApiResult<Unit>

    /** The channel's currency accounts — viewer balances + lifetime totals. First page only here. */
    suspend fun accounts(
        channelId: String,
        page: Int,
        pageSize: Int,
    ): ApiResult<PaginatedEnvelope<CurrencyAccountSummary>>

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

    /**
     * Full edit of an existing catalog item — a partial PATCH ([request], null fields unchanged) that returns the
     * item as the backend actually saved it, so a rejected or clamped field never shows as if it applied.
     */
    suspend fun updateCatalogItem(
        channelId: String,
        itemId: String,
        request: UpdateCatalogItemBody,
    ): ApiResult<CatalogItem>

    /** Delete a catalog item ([itemId]) permanently. */
    suspend fun deleteCatalogItem(channelId: String, itemId: String): ApiResult<Unit>

    /**
     * The real, backend-counted blast radius of deleting this catalog item (S-CONSEQ). The confirm dialog MUST call
     * this and render the result before the destructive save can proceed; nothing is counted client-side, and
     * a failed lookup surfaces as a failure rather than as a reassuring zero.
     */
    suspend fun catalogItemBlastRadius(
        channelId: String,
        itemId: String,
    ): ApiResult<BlastRadiusSummary>

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

    /** Get a single savings jar's detail (includes membership list). */
    suspend fun getJar(channelId: String, jarId: String): ApiResult<SavingsJarDetail>

    /**
     * Owner-only partial edit of a jar's own fields — a PATCH ([request], null fields unchanged) that returns the
     * jar as the backend actually saved it.
     */
    suspend fun updateJar(
        channelId: String,
        jarId: String,
        request: UpdateSavingsJarBody,
    ): ApiResult<SavingsJar>

    /** Owner-only permanent delete of a jar (soft delete). */
    suspend fun deleteJar(channelId: String, jarId: String): ApiResult<Unit>

    /**
     * The real, backend-counted blast radius of deleting this jar (S-CONSEQ): the other member channels who lose
     * access and the recorded movement history that stops being reachable. The confirm dialog MUST call this and
     * render the result before the destructive delete can proceed.
     */
    suspend fun jarBlastRadius(channelId: String, jarId: String): ApiResult<BlastRadiusSummary>

    /** Invite another channel (broadcaster) to join a savings jar. */
    suspend fun inviteChannel(channelId: String, jarId: String, request: InviteChannelBody): ApiResult<SavingsJarMembership>

    /** Accept a pending jar membership invitation. */
    suspend fun acceptMembership(channelId: String, membershipId: String): ApiResult<SavingsJarMembership>

    /** Revoke/remove a jar membership. */
    suspend fun removeMembership(channelId: String, membershipId: String): ApiResult<Unit>

    /** Contribute [amount] from a viewer's account into the jar. */
    suspend fun contribute(channelId: String, jarId: String, request: AdminJarContributeBody): ApiResult<Unit>

    /** Withdraw [amount] from the jar to a viewer's account. */
    suspend fun withdraw(channelId: String, jarId: String, request: AdminJarWithdrawBody): ApiResult<Unit>

    /** Jar movement history — first 50 entries. */
    suspend fun jarHistory(channelId: String, jarId: String): ApiResult<List<JarMovement>>
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

    override suspend fun leaderboardConfigs(channelId: String): ApiResult<List<LeaderboardConfig>> =
        client.getEnvelope("api/v1/channels/$channelId/economy/leaderboards/configs")

    override suspend fun upsertLeaderboardConfig(
        channelId: String,
        request: UpsertLeaderboardConfigBody,
    ): ApiResult<LeaderboardConfig> =
        client.putEnvelope("api/v1/channels/$channelId/economy/leaderboards/configs", request)

    override suspend fun deleteLeaderboardConfig(channelId: String, configId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/economy/leaderboards/configs/$configId")

    override suspend fun leaderboardConfigBlastRadius(
        channelId: String,
        configId: String,
    ): ApiResult<BlastRadiusSummary> =
        client.getEnvelope(
            "api/v1/channels/$channelId/economy/leaderboards/configs/$configId/blast-radius"
        )

    override suspend fun optOutOfLeaderboards(channelId: String, viewerUserId: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/economy/leaderboards/opt-out/$viewerUserId")

    override suspend fun optInToLeaderboards(channelId: String, viewerUserId: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/economy/leaderboards/opt-in/$viewerUserId")

    // Flat PaginatedResponse like the other lists — read with getDirect. First page only; the pager layers later.
    override suspend fun accounts(
        channelId: String,
        page: Int,
        pageSize: Int,
    ): ApiResult<PaginatedEnvelope<CurrencyAccountSummary>> =
        client.getDirect(
            "api/v1/channels/$channelId/economy/accounts?page=$page&pageSize=$pageSize"
        )

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
        // Walk every page so the whole catalog shows — flat `{ data, hasMore, nextPage }`.
        client.getAllPages { page -> "api/v1/channels/$channelId/economy/catalog?page=$page&pageSize=100" }

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

    override suspend fun updateCatalogItem(
        channelId: String,
        itemId: String,
        request: UpdateCatalogItemBody,
    ): ApiResult<CatalogItem> =
        client.patchEnvelope("api/v1/channels/$channelId/economy/catalog/$itemId", request)

    override suspend fun deleteCatalogItem(channelId: String, itemId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/economy/catalog/$itemId")

    override suspend fun catalogItemBlastRadius(
        channelId: String,
        itemId: String,
    ): ApiResult<BlastRadiusSummary> =
        client.getEnvelope("api/v1/channels/$channelId/economy/catalog/$itemId/blast-radius")

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

    override suspend fun getJar(channelId: String, jarId: String): ApiResult<SavingsJarDetail> =
        client.getEnvelope("api/v1/channels/$channelId/economy/jars/$jarId")

    override suspend fun updateJar(
        channelId: String,
        jarId: String,
        request: UpdateSavingsJarBody,
    ): ApiResult<SavingsJar> =
        client.patchEnvelope("api/v1/channels/$channelId/economy/jars/$jarId", request)

    override suspend fun deleteJar(channelId: String, jarId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/economy/jars/$jarId")

    override suspend fun jarBlastRadius(channelId: String, jarId: String): ApiResult<BlastRadiusSummary> =
        client.getEnvelope("api/v1/channels/$channelId/economy/jars/$jarId/blast-radius")

    override suspend fun inviteChannel(
        channelId: String,
        jarId: String,
        request: InviteChannelBody,
    ): ApiResult<SavingsJarMembership> =
        client.postEnvelope("api/v1/channels/$channelId/economy/jars/$jarId/invite", request)

    override suspend fun acceptMembership(
        channelId: String,
        membershipId: String,
    ): ApiResult<SavingsJarMembership> =
        client.postEnvelope(
            "api/v1/channels/$channelId/economy/jars/memberships/$membershipId/accept",
            Unit,
        )

    override suspend fun removeMembership(channelId: String, membershipId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/economy/jars/memberships/$membershipId")

    override suspend fun contribute(
        channelId: String,
        jarId: String,
        request: AdminJarContributeBody,
    ): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/economy/jars/$jarId/contribute", request)

    override suspend fun withdraw(
        channelId: String,
        jarId: String,
        request: AdminJarWithdrawBody,
    ): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/economy/jars/$jarId/withdraw", request)

    override suspend fun jarHistory(channelId: String, jarId: String): ApiResult<List<JarMovement>> =
        when (
            val page: ApiResult<PaginatedEnvelope<JarMovement>> =
                client.getDirect(
                    "api/v1/channels/$channelId/economy/jars/$jarId/history?page=1&pageSize=50"
                )
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }
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
data class UpdateCatalogItemBody(
    // Partial patch of a store catalog item (backend UpdateCatalogItemRequest) — every field nullable, null =
    // unchanged. Was toggle-only (isEnabled), so a store item could never be edited from the dashboard.
    val name: String? = null,
    val description: String? = null,
    val cost: Long? = null,
    val iconUrl: String? = null,
    val isEnabled: Boolean? = null,
    val permission: String? = null,
    val pipelineId: String? = null,
    val cooldownSeconds: Int? = null,
    val cooldownPerUser: Boolean? = null,
    val stockLimit: Int? = null,
    val maxPerViewerPerStream: Int? = null,
    val sortOrder: Int? = null,
)

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

/**
 * A partial jar update (backend `UpdateSavingsJarRequest`) — every field nullable, null = unchanged. Owner-only.
 */
@Serializable
data class UpdateSavingsJarBody(
    val name: String? = null,
    val description: String? = null,
    val goalAmount: Long? = null,
    val iconUrl: String? = null,
    val isOpen: Boolean? = null,
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
    // The viewer's internal platform-identity ULID (backend internalUserId) — addresses the analytics profile.
    val internalUserId: String? = null,
    val displayName: String = "",
    val points: Long = 0,
)

/**
 * One configured leaderboard (backend `LeaderboardConfigDto`) — the channel's leaderboard management surface
 * (list/create/edit/delete) plus the read the Economy page's primary-ranking card addresses by [id].
 * [scope] is `"channel"` or `"jar"` ([jarId] set only for the latter); [metric] is `"balance"`, `"earned"`, or
 * `"spent"`; [period] is `"alltime"`, `"daily"`, `"weekly"`, or `"monthly"`.
 */
@Serializable
data class LeaderboardConfig(
    val id: String = "",
    val jarId: String? = null,
    val metric: String = "",
    val scope: String = "",
    val period: String = "",
    val isPublic: Boolean = false,
    val topN: Int = 0,
    val createdAt: String = "",
    val updatedAt: String = "",
)

/**
 * A leaderboard config create/edit (backend `UpsertLeaderboardConfigRequest`). [id] null = create; the backend
 * upserts by id when set.
 */
@Serializable
data class UpsertLeaderboardConfigBody(
    val id: String? = null,
    val metric: String,
    val scope: String,
    val period: String,
    val isPublic: Boolean,
    val topN: Int,
    val jarId: String? = null,
)

/**
 * One viewer's currency account (backend `CurrencyAccountDto`) — the account-admin row. camelCase mirror; the
 * Economy page reads the balance + lifetime totals + frozen flag. [viewerDisplayName] / [viewerAvatarUrl] are
 * the backend's live join against the Users table (never stored on the account itself, so a Twitch name/avatar
 * change is always reflected); [lastActivityAt] is the ISO-8601 last-movement time, or null.
 */
@Serializable
data class CurrencyAccountSummary(
    val id: String = "",
    val viewerUserId: String = "",
    val viewerTwitchUserId: String = "",
    val viewerDisplayName: String = "",
    val viewerAvatarUrl: String? = null,
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
    // Per-source bonus multipliers (backend BonusConfig, an arbitrary JSON object; e.g. 2x for subs).
    val bonusConfig: kotlinx.serialization.json.JsonObject? = null,
)

/**
 * One immutable ledger movement (backend `CurrencyLedgerEntryDto`). camelCase mirror; [entryType] / [sourceType]
 * are opaque tokens — the UI displays them as-is. [amount] is signed (positive = credit, negative = debit).
 */
@Serializable
data class CurrencyLedgerEntry(
    val id: Long = 0,
    val tenantPosition: Long = 0,
    val accountId: String = "",
    val viewerUserId: String = "",
    val amount: Long = 0,
    val balanceAfter: Long = 0,
    val entryType: String = "",
    val sourceType: String? = null,
    val sourceId: String? = null,
    val relatedEntryId: Long? = null,
    val eventId: String? = null,
    val reason: String? = null,
    val actorUserId: String? = null,
    val createdAt: String = "",
)

// TransferBody is declared in ParticipantApi.kt (same package) and shared here.

/** Savings jar detail including the jar itself and its current memberships. */
@Serializable
data class SavingsJarDetail(
    val id: String = "",
    val ownerBroadcasterId: String = "",
    val name: String = "",
    val description: String? = null,
    val goalAmount: Long? = null,
    val balance: Long = 0,
    val iconUrl: String? = null,
    val isOpen: Boolean = false,
    val maxWithdrawalPerChannel: Long? = null,
    val createdAt: String = "",
    val updatedAt: String = "",
    val memberships: List<SavingsJarMembership> = emptyList(),
)

/** A channel's membership in a savings jar (backend `SavingsJarMembershipDto`). */
@Serializable
data class SavingsJarMembership(
    val id: String = "",
    val jarId: String = "",
    val memberBroadcasterId: String = "",
    val role: String = "",
    val status: String = "",
    val contributionCapPerStream: Long? = null,
    val withdrawalCap: Long? = null,
    val invitedByBroadcasterId: String? = null,
    val acceptedAt: String? = null,
)

/** One audited jar movement entry (backend `JarMovementDto`). */
@Serializable
data class JarMovement(
    val id: Long = 0,
    val jarId: String = "",
    val sourceBroadcasterId: String = "",
    val contributorUserId: String? = null,
    val amount: Long = 0,
    val movementType: String = "",
    val jarBalanceAfter: Long = 0,
    val ledgerEntryId: Long? = null,
    val actorUserId: String? = null,
    val createdAt: String = "",
)

/** Invite a channel broadcaster to join a savings jar. */
@Serializable
data class InviteChannelBody(
    val invitedBroadcasterId: String,
    val role: String,
    val contributionCapPerStream: Long? = null,
    val withdrawalCap: Long? = null,
)

/** Admin contribute on behalf of a viewer (backend `JarContributeRequest`). [contributorUserId] = viewer platform User GUID. */
@Serializable
data class AdminJarContributeBody(val contributorUserId: String, val amount: Long)

/** Admin withdraw from a jar to a viewer's account (backend `JarWithdrawRequest`). [targetViewerUserId] = viewer platform User GUID. */
@Serializable
data class AdminJarWithdrawBody(val targetViewerUserId: String, val amount: Long)

// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.economy.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.CatalogItem
import bot.nomnomz.dashboard.core.network.CatalogPurchase
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CreateCatalogItemBody
import bot.nomnomz.dashboard.core.network.CreateSavingsJarBody
import bot.nomnomz.dashboard.core.network.CurrencyAccountSummary
import bot.nomnomz.dashboard.core.network.CurrencyConfig
import bot.nomnomz.dashboard.core.network.EarningRule
import bot.nomnomz.dashboard.core.network.EconomyApi
import bot.nomnomz.dashboard.core.network.LeaderboardEntry
import bot.nomnomz.dashboard.core.network.SavingsJar
import bot.nomnomz.dashboard.core.network.UpsertCurrencyConfig
import bot.nomnomz.dashboard.core.network.UpsertEarningRuleBody
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Economy page's state-holder (economy.md §4 — the channel's currency definition + the points leaderboard).
// Resolves the active channel, then loads its real currency config and its top holders from the backend (no
// fabricated balances). The screen renders [state]: it edits a local form seeded from the loaded config and calls
// [save] to write the whole config through; the leaderboard is read-only. A retry / reconnect calls [load] again.
// The resolved channel id is cached from [load] so [save] reuses it without re-resolving.
class EconomyController(
    private val channelsApi: ChannelsApi,
    private val economyApi: EconomyApi,
) {
    private val _state: MutableStateFlow<EconomyState> = MutableStateFlow(EconomyState.Loading)

    /** The page render state: loading / ready (config + leaderboard) / error. */
    val state: StateFlow<EconomyState> = _state.asStateFlow()

    /** The channel resolved by the last successful [load]; [save] targets it without re-resolving. */
    private var channelId: String? = null

    /** Resolve the active channel, then load its currency config and points leaderboard. */
    suspend fun load() {
        _state.value = EconomyState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = EconomyState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id

        val config: CurrencyConfig? =
            when (val result: ApiResult<CurrencyConfig?> = economyApi.config(channel.id)) {
                is ApiResult.Failure -> {
                    _state.value = EconomyState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        val leaderboard: List<LeaderboardEntry> =
            when (
                val result: ApiResult<List<LeaderboardEntry>> =
                    economyApi.leaderboard(channel.id, LEADERBOARD_TOP)
            ) {
                is ApiResult.Failure -> {
                    _state.value = EconomyState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        // The account-admin list (viewer balances). A failure here must NOT blank the page — config + leaderboard
        // loaded fine — so it degrades to an empty list rather than erroring the whole screen.
        val accounts: List<CurrencyAccountSummary> =
            when (val result: ApiResult<List<CurrencyAccountSummary>> = economyApi.accounts(channel.id)) {
                is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }

        // The earning rules (how viewers earn). Same resilience contract — a failure degrades to an empty list
        // rather than erroring the whole page.
        val earningRules: List<EarningRule> =
            when (val result: ApiResult<List<EarningRule>> = economyApi.earningRules(channel.id)) {
                is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }

        // The store catalog (items viewers buy). Same resilience contract — a failure degrades to an empty list.
        val catalog: List<CatalogItem> =
            when (val result: ApiResult<List<CatalogItem>> = economyApi.catalog(channel.id)) {
                is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }

        // Community savings jars. Same resilience contract.
        val savingsJars: List<SavingsJar> =
            when (val result: ApiResult<List<SavingsJar>> = economyApi.savingsJars(channel.id)) {
                is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }

        // Catalog purchase history. Same resilience contract.
        val catalogPurchases: List<CatalogPurchase> =
            when (val result: ApiResult<List<CatalogPurchase>> = economyApi.catalogPurchases(channel.id)) {
                is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }

        _state.value =
            EconomyState.Ready(
                // A null config means the economy was never set up; seed the form with sensible defaults so the
                // operator can create it. The first save establishes the real config.
                config = config ?: CurrencyConfig(),
                configured = config != null,
                leaderboard = leaderboard,
                accounts = accounts,
                earningRules = earningRules,
                catalog = catalog,
                savingsJars = savingsJars,
                catalogPurchases = catalogPurchases,
            )
    }

    /**
     * Persist [config] for the loaded channel as a full currency-config upsert. The backend echoes the saved
     * values, which become the new loaded baseline ([EconomyState.Ready.justSaved] flags the confirmation) and
     * mark the economy configured. A failure surfaces on the current Ready state without discarding the in-progress
     * edit or the loaded leaderboard. No-ops when no channel is loaded yet (the form is only shown once Ready).
     */
    suspend fun save(config: CurrencyConfig) {
        val target: String = channelId ?: return
        val current: EconomyState = _state.value
        if (current !is EconomyState.Ready) return

        _state.value = current.copy(saving = true, justSaved = false, saveError = null)

        val update: UpsertCurrencyConfig =
            UpsertCurrencyConfig(
                currencyName = config.currencyName,
                currencyNamePlural = config.currencyNamePlural?.takeIf { it.isNotBlank() },
                iconUrl = config.iconUrl?.takeIf { it.isNotBlank() },
                isEnabled = config.isEnabled,
                startingBalance = config.startingBalance,
                maxBalance = config.maxBalance,
                decimalPlaces = config.decimalPlaces,
            )

        _state.value =
            when (val result: ApiResult<CurrencyConfig> = economyApi.updateConfig(target, update)) {
                is ApiResult.Failure ->
                    current.copy(saving = false, justSaved = false, saveError = result.error.message)
                is ApiResult.Ok ->
                    current.copy(
                        config = result.value,
                        configured = true,
                        saving = false,
                        justSaved = true,
                        saveError = null,
                    )
            }
    }

    /**
     * Freeze or unfreeze a viewer's account ([frozen]), then reload so the row reflects the new state. A frozen
     * account can neither earn nor spend. No-ops when no channel is loaded; surfaces the error on the current
     * Ready state (the page's [EconomyState.Ready.saveError] slot) on failure without losing the loaded page.
     */
    suspend fun freezeAccount(viewerUserId: String, frozen: Boolean) {
        val target: String = channelId ?: return
        when (
            val result: ApiResult<Unit> = economyApi.freezeAccount(target, viewerUserId, frozen)
        ) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> {
                val current: EconomyState = _state.value
                if (current is EconomyState.Ready) {
                    _state.value = current.copy(saveError = result.error.message)
                }
            }
        }
    }

    /**
     * Enable or disable a catalog item ([enabled]), then reload so the row reflects the new state. No-ops when no
     * channel is loaded; surfaces the error on the current Ready state's [EconomyState.Ready.saveError] on failure.
     */
    suspend fun setCatalogItemEnabled(itemId: String, enabled: Boolean) {
        val target: String = channelId ?: return
        when (
            val result: ApiResult<Unit> = economyApi.setCatalogItemEnabled(target, itemId, enabled)
        ) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> {
                val current: EconomyState = _state.value
                if (current is EconomyState.Ready) {
                    _state.value = current.copy(saveError = result.error.message)
                }
            }
        }
    }

    /**
     * Toggle the earning rule identified by [source] (e.g. `"chat_message"`) — flips its [isEnabled] flag while
     * keeping all other fields unchanged from the currently-loaded rule. No-ops when no rule for [source] is loaded.
     * Reloads on success; surfaces the error on the Ready state on failure.
     */
    suspend fun toggleEarningRule(source: String, isEnabled: Boolean) {
        val channel: String = channelId ?: return
        val current: EconomyState = _state.value
        if (current !is EconomyState.Ready) return
        val rule: EarningRule = current.earningRules.firstOrNull { it.source == source } ?: return
        val body: UpsertEarningRuleBody =
            UpsertEarningRuleBody(
                source = rule.source,
                isEnabled = isEnabled,
                rate = rule.rate,
                unitWindowSeconds = rule.unitWindowSeconds,
                perWindowCap = rule.perWindowCap,
                perStreamCap = rule.perStreamCap,
                minRoleLevel = rule.minRoleLevel,
            )
        afterWrite(economyApi.upsertEarningRule(channel, body))
    }

    /**
     * Create a new catalog item with [request] and reload so it appears in the store list. Surfaces the error on the
     * Ready state on failure.
     */
    suspend fun createCatalogItem(request: CreateCatalogItemBody) {
        val channel: String = channelId ?: return
        // postEnvelope returns the saved item — we don't need it here; reload gives us the full list.
        when (val result: ApiResult<CatalogItem> = economyApi.createCatalogItem(channel, request)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> {
                val current: EconomyState = _state.value
                if (current is EconomyState.Ready) {
                    _state.value = current.copy(saveError = result.error.message)
                }
            }
        }
    }

    /** Delete the catalog item [itemId], then reload so it drops off the list. Surfaces the error on failure. */
    suspend fun deleteCatalogItem(itemId: String) {
        val channel: String = channelId ?: return
        afterWrite(economyApi.deleteCatalogItem(channel, itemId))
    }

    /**
     * Create a new savings jar with [request] and reload. Surfaces the error on the Ready state on failure.
     */
    suspend fun createSavingsJar(request: CreateSavingsJarBody) {
        val channel: String = channelId ?: return
        when (val result: ApiResult<SavingsJar> = economyApi.createSavingsJar(channel, request)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> {
                val current: EconomyState = _state.value
                if (current is EconomyState.Ready) {
                    _state.value = current.copy(saveError = result.error.message)
                }
            }
        }
    }

    /** Admin-adjust a viewer's balance (positive = credit, negative = debit). Reloads on success. */
    suspend fun adjustAccount(viewerUserId: String, amount: Long, reason: String?) {
        val channel: String = channelId ?: return
        afterWrite(economyApi.adjustAccount(channel, viewerUserId, amount, reason))
    }

    /** Refund a catalog purchase — credits the cost back to the buyer. Reloads on success. */
    suspend fun refundPurchase(purchaseId: Long) {
        val channel: String = channelId ?: return
        afterWrite(economyApi.refundPurchase(channel, purchaseId))
    }

    // Reload on success; on failure surface the message on the current Ready state without losing the loaded page.
    private suspend fun afterWrite(result: ApiResult<*>) {
        when (result) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> {
                val current: EconomyState = _state.value
                if (current is EconomyState.Ready) {
                    _state.value = current.copy(saveError = result.error.message)
                }
            }
        }
    }

    private companion object {
        // The top-holders window the Economy page surfaces — a fixed, bounded read (the backend caps it too).
        const val LEADERBOARD_TOP: Int = 25
    }
}

/** The Economy page render state. */
sealed interface EconomyState {
    data object Loading : EconomyState

    /**
     * The loaded currency config plus the read-only leaderboard. [configured] is false when the economy has never
     * been set up (the form seeds from defaults and the first save creates it). The in-flight save signals: [saving]
     * while a write is pending, [justSaved] right after a successful save, and [saveError] when the last save failed.
     * The screen seeds its editable form from [config] and renders [leaderboard] below it.
     */
    data class Ready(
        val config: CurrencyConfig,
        val configured: Boolean,
        val leaderboard: List<LeaderboardEntry>,
        val accounts: List<CurrencyAccountSummary> = emptyList(),
        val earningRules: List<EarningRule> = emptyList(),
        val catalog: List<CatalogItem> = emptyList(),
        val savingsJars: List<SavingsJar> = emptyList(),
        val catalogPurchases: List<CatalogPurchase> = emptyList(),
        val saving: Boolean = false,
        val justSaved: Boolean = false,
        val saveError: String? = null,
    ) : EconomyState

    data class Error(val detail: String) : EconomyState
}

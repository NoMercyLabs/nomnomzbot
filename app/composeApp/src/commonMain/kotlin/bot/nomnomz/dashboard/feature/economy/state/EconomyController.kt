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
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CurrencyAccountSummary
import bot.nomnomz.dashboard.core.network.CurrencyConfig
import bot.nomnomz.dashboard.core.network.EconomyApi
import bot.nomnomz.dashboard.core.network.LeaderboardEntry
import bot.nomnomz.dashboard.core.network.UpsertCurrencyConfig
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

        _state.value =
            EconomyState.Ready(
                // A null config means the economy was never set up; seed the form with sensible defaults so the
                // operator can create it. The first save establishes the real config.
                config = config ?: CurrencyConfig(),
                configured = config != null,
                leaderboard = leaderboard,
                accounts = accounts,
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
        val saving: Boolean = false,
        val justSaved: Boolean = false,
        val saveError: String? = null,
    ) : EconomyState

    data class Error(val detail: String) : EconomyState
}

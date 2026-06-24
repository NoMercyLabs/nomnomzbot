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

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CurrencyConfig
import bot.nomnomz.dashboard.core.network.EconomyApi
import bot.nomnomz.dashboard.core.network.LeaderboardEntry
import bot.nomnomz.dashboard.core.network.UpsertCurrencyConfig
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the Economy page state machine the screen renders: resolve the active channel, then surface the real
// currency config and the real top-holders leaderboard — or an error if a step fails — and persist the operator's
// config edits as a full upsert, adopting the backend's echoed config. The screen is a pure projection of this, so
// testing it proves the page shows the channel's real economy (no fabricated balances) and degrades cleanly.
class EconomyControllerTest {

    private val loadedConfig =
        CurrencyConfig(
            id = "cfg1",
            broadcasterId = "ch1",
            currencyName = "Crumbs",
            currencyNamePlural = "Crumbs",
            iconUrl = null,
            isEnabled = true,
            startingBalance = 100,
            maxBalance = 1_000_000,
            decimalPlaces = 0,
            createdAt = "2026-06-01T00:00:00Z",
            updatedAt = "2026-06-20T00:00:00Z",
        )

    private val leaderboard =
        listOf(
            LeaderboardEntry(rank = 1, userId = "u1", displayName = "Stoney_Eagle", points = 4200),
            LeaderboardEntry(rank = 2, userId = "u2", displayName = "Nibbles", points = 1300),
        )

    @Test
    fun load_surfaces_the_real_config_and_leaderboard_on_success() = runTest {
        val economyApi =
            FakeEconomyApi(
                configResult = ApiResult.Ok(loadedConfig),
                leaderboardResult = ApiResult.Ok(leaderboard),
            )
        val controller =
            EconomyController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), economyApi)

        controller.load()

        val state: EconomyState = controller.state.value
        assertTrue(state is EconomyState.Ready)
        val ready: EconomyState.Ready = state as EconomyState.Ready
        assertEquals(loadedConfig, ready.config)
        assertTrue(ready.configured)
        assertEquals(leaderboard, ready.leaderboard)
        // The leaderboard read is addressed to the resolved channel.
        assertEquals("ch1", economyApi.lastLeaderboardChannelId)
    }

    @Test
    fun load_seeds_a_default_form_when_the_economy_is_not_configured() = runTest {
        // A null config means the economy was never set up — the page must still render a (default) form so the
        // operator can create it, flagged not-configured, with whatever leaderboard exists (empty here).
        val controller =
            EconomyController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeEconomyApi(
                    configResult = ApiResult.Ok(null),
                    leaderboardResult = ApiResult.Ok(emptyList()),
                ),
            )

        controller.load()

        val state: EconomyState = controller.state.value
        assertTrue(state is EconomyState.Ready)
        val ready: EconomyState.Ready = state as EconomyState.Ready
        assertEquals(CurrencyConfig(), ready.config)
        assertEquals(false, ready.configured)
        assertTrue(ready.leaderboard.isEmpty())
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            EconomyController(
                FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                FakeEconomyApi(
                    configResult = ApiResult.Ok(loadedConfig),
                    leaderboardResult = ApiResult.Ok(leaderboard),
                ),
            )

        controller.load()

        assertTrue(controller.state.value is EconomyState.Error)
    }

    @Test
    fun load_errors_when_the_config_call_fails() = runTest {
        val controller =
            EconomyController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeEconomyApi(
                    configResult = ApiResult.Failure(ApiError(500, "ERR", "boom")),
                    leaderboardResult = ApiResult.Ok(leaderboard),
                ),
            )

        controller.load()

        assertTrue(controller.state.value is EconomyState.Error)
    }

    @Test
    fun load_errors_when_the_leaderboard_call_fails() = runTest {
        val controller =
            EconomyController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeEconomyApi(
                    configResult = ApiResult.Ok(loadedConfig),
                    leaderboardResult = ApiResult.Failure(ApiError(500, "ERR", "boom")),
                ),
            )

        controller.load()

        assertTrue(controller.state.value is EconomyState.Error)
    }

    @Test
    fun save_sends_the_full_upsert_and_adopts_the_backend_echoed_config() = runTest {
        // The backend canonicalizes and echoes the saved config back; the controller adopts THAT, not what the user
        // typed. The echo here renames the currency and clamps the starting balance, proving the controller
        // surfaces the persisted server values rather than the request, while keeping the loaded leaderboard.
        val echoed =
            loadedConfig.copy(
                currencyName = "Cookies",
                currencyNamePlural = "Cookies",
                startingBalance = 50,
                updatedAt = "2026-06-24T00:00:00Z",
            )
        val economyApi =
            FakeEconomyApi(
                configResult = ApiResult.Ok(loadedConfig),
                leaderboardResult = ApiResult.Ok(leaderboard),
                updateResult = ApiResult.Ok(echoed),
            )
        val controller =
            EconomyController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), economyApi)
        controller.load()

        val edited: CurrencyConfig = loadedConfig.copy(currencyName = "Cookies", startingBalance = 250)
        controller.save(edited)

        // The whole config is sent for the resolved channel as a full UpsertCurrencyConfig carrying the edits.
        assertEquals("ch1", economyApi.lastUpdateChannelId)
        assertEquals(
            UpsertCurrencyConfig(
                currencyName = "Cookies",
                currencyNamePlural = "Crumbs",
                iconUrl = null,
                isEnabled = true,
                startingBalance = 250,
                maxBalance = 1_000_000,
                decimalPlaces = 0,
            ),
            economyApi.lastUpdate,
        )

        // State now holds the backend echo, flags the save, keeps the leaderboard, and carries no error.
        val state: EconomyState = controller.state.value
        assertTrue(state is EconomyState.Ready)
        val ready: EconomyState.Ready = state as EconomyState.Ready
        assertEquals(echoed, ready.config)
        assertTrue(ready.configured)
        assertTrue(ready.justSaved)
        assertEquals(false, ready.saving)
        assertNull(ready.saveError)
        assertEquals(leaderboard, ready.leaderboard)
    }

    @Test
    fun save_failure_surfaces_the_error_without_losing_the_loaded_config_or_leaderboard() = runTest {
        val economyApi =
            FakeEconomyApi(
                configResult = ApiResult.Ok(loadedConfig),
                leaderboardResult = ApiResult.Ok(leaderboard),
                updateResult = ApiResult.Failure(ApiError(500, "ERR", "save boom")),
            )
        val controller =
            EconomyController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), economyApi)
        controller.load()

        controller.save(loadedConfig.copy(startingBalance = 9))

        // The page stays Ready on the loaded config + leaderboard (no data loss) but surfaces the save error.
        val state: EconomyState = controller.state.value
        assertTrue(state is EconomyState.Ready)
        val ready: EconomyState.Ready = state as EconomyState.Ready
        assertEquals(loadedConfig, ready.config)
        assertEquals(leaderboard, ready.leaderboard)
        assertEquals("save boom", ready.saveError)
        assertEquals(false, ready.saving)
        assertEquals(false, ready.justSaved)
    }
}

private class FakeChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result
}

private class FakeEconomyApi(
    private val configResult: ApiResult<CurrencyConfig?>,
    private val leaderboardResult: ApiResult<List<LeaderboardEntry>>,
    private val updateResult: ApiResult<CurrencyConfig> = ApiResult.Ok(CurrencyConfig()),
) : EconomyApi {
    var lastUpdate: UpsertCurrencyConfig? = null
        private set

    var lastUpdateChannelId: String? = null
        private set

    var lastLeaderboardChannelId: String? = null
        private set

    override suspend fun config(channelId: String): ApiResult<CurrencyConfig?> = configResult

    override suspend fun updateConfig(
        channelId: String,
        update: UpsertCurrencyConfig,
    ): ApiResult<CurrencyConfig> {
        lastUpdateChannelId = channelId
        lastUpdate = update
        return updateResult
    }

    override suspend fun leaderboard(
        channelId: String,
        top: Int,
    ): ApiResult<List<LeaderboardEntry>> {
        lastLeaderboardChannelId = channelId
        return leaderboardResult
    }
}

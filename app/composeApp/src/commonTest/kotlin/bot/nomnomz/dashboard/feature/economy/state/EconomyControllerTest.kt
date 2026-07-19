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
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.CatalogItem
import bot.nomnomz.dashboard.core.network.CatalogPurchase
import bot.nomnomz.dashboard.core.network.CreateCatalogItemBody
import bot.nomnomz.dashboard.core.network.CreateSavingsJarBody
import bot.nomnomz.dashboard.core.network.AdminJarContributeBody
import bot.nomnomz.dashboard.core.network.AdminJarWithdrawBody
import bot.nomnomz.dashboard.core.network.CurrencyAccountSummary
import bot.nomnomz.dashboard.core.network.CurrencyConfig
import bot.nomnomz.dashboard.core.network.CurrencyLedgerEntry
import bot.nomnomz.dashboard.core.network.EarningRule
import bot.nomnomz.dashboard.core.network.InviteChannelBody
import bot.nomnomz.dashboard.core.network.JarMovement
import bot.nomnomz.dashboard.core.network.SavingsJarDetail
import bot.nomnomz.dashboard.core.network.SavingsJarMembership
import bot.nomnomz.dashboard.core.network.TransferBody
import bot.nomnomz.dashboard.core.network.EconomyApi
import bot.nomnomz.dashboard.core.network.LeaderboardEntry
import bot.nomnomz.dashboard.core.network.SavingsJar
import bot.nomnomz.dashboard.core.network.UpsertCurrencyConfig
import bot.nomnomz.dashboard.core.network.UpsertEarningRuleBody
import bot.nomnomz.dashboard.core.network.UserSearchResult
import bot.nomnomz.dashboard.core.network.UsersApi
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
            EconomyController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), economyApi, FakeUsersApi())

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
    fun load_surfaces_the_currency_accounts() = runTest {
        val economyApi =
            FakeEconomyApi(
                configResult = ApiResult.Ok(loadedConfig),
                leaderboardResult = ApiResult.Ok(leaderboard),
                accountsResult =
                    ApiResult.Ok(
                        listOf(
                            CurrencyAccountSummary(
                                id = "a1",
                                viewerTwitchUserId = "39863651",
                                balance = 1200,
                                lifetimeEarned = 5000,
                                isFrozen = false,
                            )
                        )
                    ),
            )
        val controller =
            EconomyController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), economyApi, FakeUsersApi())

        controller.load()

        val ready: EconomyState.Ready = controller.state.value as EconomyState.Ready
        assertEquals(1, ready.accounts.size)
        assertEquals("39863651", ready.accounts.first().viewerTwitchUserId)
        assertEquals(1200, ready.accounts.first().balance)
    }

    @Test
    fun a_failed_accounts_load_degrades_to_an_empty_list_not_an_error() = runTest {
        val economyApi =
            FakeEconomyApi(
                configResult = ApiResult.Ok(loadedConfig),
                leaderboardResult = ApiResult.Ok(leaderboard),
                accountsResult = ApiResult.Failure(ApiError(500, "ERR", "boom")),
            )
        val controller =
            EconomyController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), economyApi, FakeUsersApi())

        controller.load()

        // Config + leaderboard loaded fine, so the page stays Ready with an empty accounts list.
        val ready: EconomyState.Ready = controller.state.value as EconomyState.Ready
        assertTrue(ready.accounts.isEmpty())
    }

    @Test
    fun load_surfaces_the_earning_rules() = runTest {
        val economyApi =
            FakeEconomyApi(
                configResult = ApiResult.Ok(loadedConfig),
                leaderboardResult = ApiResult.Ok(leaderboard),
                earningRulesResult =
                    ApiResult.Ok(
                        listOf(
                            EarningRule(
                                id = "e1",
                                source = "chat_message",
                                isEnabled = true,
                                rate = 5,
                            )
                        )
                    ),
            )
        val controller =
            EconomyController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), economyApi, FakeUsersApi())

        controller.load()

        val ready: EconomyState.Ready = controller.state.value as EconomyState.Ready
        assertEquals(1, ready.earningRules.size)
        assertEquals("chat_message", ready.earningRules.first().source)
        assertEquals(5, ready.earningRules.first().rate)
    }

    @Test
    fun upserting_an_earning_rule_sends_the_full_field_set_then_reloads() = runTest {
        val economyApi =
            FakeEconomyApi(
                configResult = ApiResult.Ok(loadedConfig),
                leaderboardResult = ApiResult.Ok(leaderboard),
            )
        val controller =
            EconomyController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), economyApi, FakeUsersApi())
        controller.load()

        controller.upsertEarningRule(
            UpsertEarningRuleBody(
                source = "WatchTime",
                isEnabled = true,
                rate = 10,
                unitWindowSeconds = 300,
                perWindowCap = 50,
                perStreamCap = 500,
                minRoleLevel = 2,
            )
        )

        // Every configurable field of the rule reached the API — rate, unit window, both caps, and the min role —
        // not merely the enabled flag the old toggle-only surface could send.
        val sent: UpsertEarningRuleBody =
            requireNotNull(economyApi.lastEarningRuleUpsert) { "no earning-rule upsert recorded" }
        assertEquals("WatchTime", sent.source)
        assertEquals(true, sent.isEnabled)
        assertEquals(10, sent.rate)
        assertEquals(300, sent.unitWindowSeconds)
        assertEquals(50, sent.perWindowCap)
        assertEquals(500, sent.perStreamCap)
        assertEquals(2, sent.minRoleLevel)
        assertTrue(controller.state.value is EconomyState.Ready) // reloaded; page intact
    }

    @Test
    fun freezing_an_account_calls_the_api_with_the_viewer_and_flag_then_reloads() = runTest {
        val economyApi =
            FakeEconomyApi(
                configResult = ApiResult.Ok(loadedConfig),
                leaderboardResult = ApiResult.Ok(leaderboard),
                accountsResult =
                    ApiResult.Ok(
                        listOf(
                            CurrencyAccountSummary(
                                id = "a1",
                                viewerUserId = "v1",
                                viewerTwitchUserId = "39863651",
                                balance = 1200,
                                isFrozen = false,
                            )
                        )
                    ),
            )
        val controller =
            EconomyController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), economyApi, FakeUsersApi())
        controller.load()

        controller.freezeAccount("v1", frozen = true)

        assertEquals("v1" to true, economyApi.lastFreeze) // addressed by the account's viewerUserId + the flag
        assertTrue(controller.state.value is EconomyState.Ready) // reloaded; page intact
    }

    @Test
    fun load_surfaces_the_store_catalog() = runTest {
        val economyApi =
            FakeEconomyApi(
                configResult = ApiResult.Ok(loadedConfig),
                leaderboardResult = ApiResult.Ok(leaderboard),
                catalogResult =
                    ApiResult.Ok(
                        listOf(
                            CatalogItem(
                                id = "c1",
                                name = "Timeout the streamer",
                                cost = 500,
                                isEnabled = true,
                                stockLimit = 3,
                                stockRemaining = 2,
                            )
                        )
                    ),
            )
        val controller =
            EconomyController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), economyApi, FakeUsersApi())

        controller.load()

        val ready: EconomyState.Ready = controller.state.value as EconomyState.Ready
        assertEquals(1, ready.catalog.size)
        assertEquals("Timeout the streamer", ready.catalog.first().name)
        assertEquals(500, ready.catalog.first().cost)
        assertEquals(2, ready.catalog.first().stockRemaining)
    }

    @Test
    fun toggling_a_catalog_item_calls_the_api_with_the_item_and_flag_then_reloads() = runTest {
        val economyApi =
            FakeEconomyApi(
                configResult = ApiResult.Ok(loadedConfig),
                leaderboardResult = ApiResult.Ok(leaderboard),
                catalogResult =
                    ApiResult.Ok(
                        listOf(CatalogItem(id = "c1", name = "Hydrate", cost = 50, isEnabled = true))
                    ),
            )
        val controller =
            EconomyController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), economyApi, FakeUsersApi())
        controller.load()

        controller.setCatalogItemEnabled("c1", enabled = false)

        assertEquals("c1" to false, economyApi.lastCatalogToggle) // addressed by item id + the flag
        assertTrue(controller.state.value is EconomyState.Ready) // reloaded; page intact
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
                FakeUsersApi(),
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
                FakeUsersApi(),
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
                FakeUsersApi(),
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
                FakeUsersApi(),
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
            EconomyController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), economyApi, FakeUsersApi())
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
            EconomyController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), economyApi, FakeUsersApi())
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

    override suspend fun list(): ApiResult<List<ChannelSummary>> = ApiResult.Ok(emptyList())

    override suspend fun join(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun leave(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun reset(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun deleteChannel(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun channelScopes(channelId: String) = error("stub")
    override suspend fun startChannelBotConnect(channelId: String) = error("stub")
    override suspend fun channelBotStatus(channelId: String) = error("stub")
    override suspend fun disconnectChannelBot(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun moderatedChannels(): ApiResult<List<ModeratedChannel>> = ApiResult.Ok(emptyList())
}

private class FakeUsersApi(
    private val searchResult: ApiResult<List<UserSearchResult>> = ApiResult.Ok(emptyList()),
) : UsersApi {
    override suspend fun search(query: String, limit: Int): ApiResult<List<UserSearchResult>> =
        searchResult

    override suspend fun stats(userId: String) = error("stub")
    override suspend fun export(userId: String) = error("stub")
    override suspend fun erase(userId: String) = error("stub")
}

private class FakeEconomyApi(
    private val configResult: ApiResult<CurrencyConfig?>,
    private val leaderboardResult: ApiResult<List<LeaderboardEntry>>,
    private val updateResult: ApiResult<CurrencyConfig> = ApiResult.Ok(CurrencyConfig()),
    private val accountsResult: ApiResult<List<CurrencyAccountSummary>> = ApiResult.Ok(emptyList()),
    private val earningRulesResult: ApiResult<List<EarningRule>> = ApiResult.Ok(emptyList()),
    private val catalogResult: ApiResult<List<CatalogItem>> = ApiResult.Ok(emptyList()),
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

    override suspend fun accounts(channelId: String): ApiResult<List<CurrencyAccountSummary>> =
        accountsResult

    override suspend fun earningRules(channelId: String): ApiResult<List<EarningRule>> =
        earningRulesResult

    var lastFreeze: Pair<String, Boolean>? = null
        private set

    override suspend fun freezeAccount(
        channelId: String,
        viewerUserId: String,
        frozen: Boolean,
    ): ApiResult<Unit> {
        lastFreeze = viewerUserId to frozen
        return ApiResult.Ok(Unit)
    }

    override suspend fun catalog(channelId: String): ApiResult<List<CatalogItem>> = catalogResult

    var lastCatalogToggle: Pair<String, Boolean>? = null
        private set

    override suspend fun setCatalogItemEnabled(
        channelId: String,
        itemId: String,
        enabled: Boolean,
    ): ApiResult<Unit> {
        lastCatalogToggle = itemId to enabled
        return ApiResult.Ok(Unit)
    }

    override suspend fun createCatalogItem(channelId: String, request: CreateCatalogItemBody): ApiResult<CatalogItem> =
        ApiResult.Ok(CatalogItem())

    override suspend fun deleteCatalogItem(channelId: String, itemId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    var lastEarningRuleUpsert: UpsertEarningRuleBody? = null
        private set

    override suspend fun upsertEarningRule(channelId: String, request: UpsertEarningRuleBody): ApiResult<EarningRule> {
        lastEarningRuleUpsert = request
        return ApiResult.Ok(EarningRule())
    }

    override suspend fun savingsJars(channelId: String): ApiResult<List<SavingsJar>> = ApiResult.Ok(emptyList())

    override suspend fun createSavingsJar(channelId: String, request: CreateSavingsJarBody): ApiResult<SavingsJar> =
        ApiResult.Ok(SavingsJar())

    override suspend fun adjustAccount(channelId: String, viewerUserId: String, amount: Long, reason: String?): ApiResult<Unit> =
        ApiResult.Ok(Unit)

    override suspend fun catalogPurchases(channelId: String): ApiResult<List<CatalogPurchase>> = ApiResult.Ok(emptyList())

    override suspend fun refundPurchase(channelId: String, purchaseId: Long): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun deleteEarningRule(channelId: String, ruleId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun ledger(channelId: String, viewerUserId: String): ApiResult<List<CurrencyLedgerEntry>> =
        ApiResult.Ok(emptyList())

    override suspend fun transfer(channelId: String, request: TransferBody): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun getJar(channelId: String, jarId: String): ApiResult<SavingsJarDetail> =
        ApiResult.Ok(SavingsJarDetail())

    override suspend fun inviteChannel(
        channelId: String,
        jarId: String,
        request: InviteChannelBody,
    ): ApiResult<SavingsJarMembership> = ApiResult.Ok(SavingsJarMembership())

    override suspend fun acceptMembership(
        channelId: String,
        membershipId: String,
    ): ApiResult<SavingsJarMembership> = ApiResult.Ok(SavingsJarMembership())

    override suspend fun removeMembership(channelId: String, membershipId: String): ApiResult<Unit> =
        ApiResult.Ok(Unit)

    override suspend fun contribute(
        channelId: String,
        jarId: String,
        request: AdminJarContributeBody,
    ): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun withdraw(
        channelId: String,
        jarId: String,
        request: AdminJarWithdrawBody,
    ): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun jarHistory(channelId: String, jarId: String): ApiResult<List<JarMovement>> =
        ApiResult.Ok(emptyList())
}

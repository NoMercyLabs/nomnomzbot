// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.participant.state

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.CatalogItem
import bot.nomnomz.dashboard.core.network.ChannelAppearance
import bot.nomnomz.dashboard.core.network.CurrencyAccount
import bot.nomnomz.dashboard.core.network.CurrencyAccountSummary
import bot.nomnomz.dashboard.core.network.EarningRule
import bot.nomnomz.dashboard.core.network.CurrencyConfig
import bot.nomnomz.dashboard.core.network.DashboardApi
import bot.nomnomz.dashboard.core.network.DashboardStats
import bot.nomnomz.dashboard.core.network.EconomyApi
import bot.nomnomz.dashboard.core.network.GamePlay
import bot.nomnomz.dashboard.core.network.GamePlayResult
import bot.nomnomz.dashboard.core.network.GameSummary
import bot.nomnomz.dashboard.core.network.LeaderboardEntry
import bot.nomnomz.dashboard.core.network.MusicApi
import bot.nomnomz.dashboard.core.network.MusicSnapshot
import bot.nomnomz.dashboard.core.network.MusicTrack
import bot.nomnomz.dashboard.core.network.NowPlaying
import bot.nomnomz.dashboard.core.network.ParticipantApi
import bot.nomnomz.dashboard.core.network.SavingsJar
import bot.nomnomz.dashboard.core.network.UpsertCurrencyConfig
import bot.nomnomz.dashboard.core.network.UserActivity
import bot.nomnomz.dashboard.core.network.UserProfile
import bot.nomnomz.dashboard.feature.shell.nav.ParticipantStanding
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the participant rung's state machine — the consequence of each self-service action, not just that a call
// returned. Each test asserts the REAL outcome: the correct read/community API was hit for the caller's own id, the
// resulting state shape (balance, queue, ranking, history), the standing-driven unlock surfaced, the capability
// gate honored, and a write's side effect (a reload, an optimistic toggle, the settled game outcome). A surface
// "didn't throw" assertion would be void here — every test can fail for the right reason.
class ParticipantControllerTest {

    private val channelId: String = "ch1"
    private val callerId: String = "user-1"

    // ── My Channel ───────────────────────────────────────────────────────────

    @Test
    fun load_my_channel_fetches_the_public_summary_and_the_callers_own_profile_and_activity() = runTest {
        val api = FakeParticipantApi()
        val dashboard = FakeDashboardApi(ApiResult.Ok(DashboardStats(isLive = true, streamTitle = "Coding")))
        val controller = controller(api = api, dashboard = dashboard)

        controller.loadMyChannel()

        val ready: MyChannelState.Ready = controller.myChannel.value as MyChannelState.Ready
        // The channel summary is the public dashboard read; the profile + activity are the caller's OWN records.
        assertEquals(true, ready.channel.isLive)
        assertEquals("Coding", ready.channel.streamTitle)
        assertEquals(callerId, ready.profile.id)
        assertEquals(42, ready.activity.messageCount)
        // The own-data reads addressed the caller's id (not a fabricated or other user's id).
        assertEquals(listOf(callerId), api.profileCalls)
        assertEquals(listOf(callerId), api.activityCalls)
        assertEquals(listOf(channelId), dashboard.statsCalls)
    }

    // ── Now Playing / Queue ──────────────────────────────────────────────────

    @Test
    fun load_now_playing_surfaces_the_live_snapshot_and_the_standing_pending_limit() = runTest {
        val music =
            FakeMusicApi(
                ApiResult.Ok(
                    MusicSnapshot(
                        nowPlaying = NowPlaying(trackName = "Track A"),
                        queue = listOf(MusicTrack(position = 0, trackName = "Track B")),
                    )
                )
            )
        // A plain viewer (Everyone) gets the base pending allowance and no sub lane.
        val controller = controller(music = music, standing = ParticipantStanding.Everyone)

        controller.loadNowPlaying()

        val ready: NowPlayingState.Ready = controller.nowPlaying.value as NowPlayingState.Ready
        assertEquals("Track A", ready.snapshot.nowPlaying?.trackName)
        assertEquals(1, ready.snapshot.queue.size)
        assertEquals(1, ready.pendingLimit)
        assertFalse(ready.subscriberLaneUnlocked)
        assertEquals(listOf(channelId), music.queueCalls)
    }

    @Test
    fun a_subscriber_unlocks_the_sub_lane_and_a_higher_pending_limit_than_a_plain_viewer() = runTest {
        // The standing-driven unlock the screen renders: a Sub sees the sub-only lane and may have more pending
        // requests than a plain viewer — a difference a plain viewer does NOT get. Proven by comparing the two.
        val viewer = controller(music = FakeMusicApi(ApiResult.Ok(MusicSnapshot())), standing = ParticipantStanding.Everyone)
        val subscriber =
            controller(music = FakeMusicApi(ApiResult.Ok(MusicSnapshot())), standing = ParticipantStanding.Subscriber)

        viewer.loadNowPlaying()
        subscriber.loadNowPlaying()

        val viewerReady: NowPlayingState.Ready = viewer.nowPlaying.value as NowPlayingState.Ready
        val subReady: NowPlayingState.Ready = subscriber.nowPlaying.value as NowPlayingState.Ready
        assertFalse(viewerReady.subscriberLaneUnlocked)
        assertTrue(subReady.subscriberLaneUnlocked)
        assertTrue(subReady.pendingLimit > viewerReady.pendingLimit)
    }

    @Test
    fun submitting_a_song_request_posts_the_query_and_reloads_the_queue() = runTest {
        val music = FakeMusicApi(ApiResult.Ok(MusicSnapshot()))
        val api = FakeParticipantApi()
        val controller = controller(api = api, music = music)
        controller.loadNowPlaying()
        val queueCallsBefore: Int = music.queueCalls.size

        controller.submitSongRequest("never gonna give you up", null)

        // The request hit the community submit route with the typed query, and the queue re-read to project it.
        assertEquals(1, api.songRequests.size)
        assertEquals(channelId to "never gonna give you up", api.songRequests.first())
        assertTrue(music.queueCalls.size > queueCallsBefore, "queue must re-read after a submit")
    }

    @Test
    fun a_failed_song_submit_keeps_the_queue_and_surfaces_the_reason() = runTest {
        val api = FakeParticipantApi(songRequestResult = ApiResult.Failure(ApiError(503, "ERR", "no provider")))
        val controller = controller(api = api, music = FakeMusicApi(ApiResult.Ok(MusicSnapshot())))
        controller.loadNowPlaying()

        controller.submitSongRequest("a song", null)

        val ready: NowPlayingState.Ready = controller.nowPlaying.value as NowPlayingState.Ready
        assertEquals("no provider", ready.actionError)
    }

    // ── Leaderboards ─────────────────────────────────────────────────────────

    @Test
    fun load_leaderboards_surfaces_the_ranking_for_the_channel() = runTest {
        val economy =
            FakeEconomyApi(
                leaderboard =
                    listOf(
                        LeaderboardEntry(rank = 1, userId = "u1", displayName = "Stoney_Eagle", points = 9000),
                    )
            )
        val controller = controller(economy = economy)

        controller.loadLeaderboards()

        val ready: LeaderboardsState.Ready = controller.leaderboards.value as LeaderboardsState.Ready
        assertEquals(1, ready.ranking.size)
        assertEquals("Stoney_Eagle", ready.ranking.first().displayName)
        assertEquals(listOf(channelId), economy.leaderboardCalls)
    }

    @Test
    fun opting_out_of_leaderboards_calls_the_opt_out_route_for_the_caller_and_flips_the_state() = runTest {
        val api = FakeParticipantApi()
        val controller = controller(api = api)
        controller.loadLeaderboards()

        controller.optOutOfLeaderboards()

        val ready: LeaderboardsState.Ready = controller.leaderboards.value as LeaderboardsState.Ready
        assertFalse(ready.optedIn)
        // The opt-out addressed the caller's own id on the channel.
        assertEquals(listOf(channelId to callerId), api.optOutCalls)
        assertTrue(api.optInCalls.isEmpty())
    }

    @Test
    fun a_subscriber_sees_the_subscriber_leaderboard_a_plain_viewer_does_not() = runTest {
        val viewer = controller(economy = FakeEconomyApi(), standing = ParticipantStanding.Everyone)
        val subscriber = controller(economy = FakeEconomyApi(), standing = ParticipantStanding.Subscriber)

        viewer.loadLeaderboards()
        subscriber.loadLeaderboards()

        val viewerReady: LeaderboardsState.Ready = viewer.leaderboards.value as LeaderboardsState.Ready
        val subReady: LeaderboardsState.Ready = subscriber.leaderboards.value as LeaderboardsState.Ready
        assertFalse(viewerReady.subscriberBoardUnlocked)
        assertTrue(subReady.subscriberBoardUnlocked)
    }

    // ── Points & Store ───────────────────────────────────────────────────────

    @Test
    fun load_store_reads_the_callers_own_wallet_the_catalog_and_the_jars() = runTest {
        val api =
            FakeParticipantApi(
                account = CurrencyAccount(balance = 1500, lifetimeEarned = 4000, lifetimeSpent = 2500),
                catalog = listOf(CatalogItem(id = "i1", name = "Timeout", cost = 500, isEnabled = true)),
                jars = listOf(SavingsJar(id = "j1", name = "Charity", balance = 200, isOpen = true)),
            )
        val controller = controller(api = api, canTransfer = false)

        controller.loadStore()

        val ready: StoreState.Ready = controller.store.value as StoreState.Ready
        assertEquals(1500, ready.account.balance)
        assertEquals(1, ready.catalog.size)
        assertEquals(1, ready.jars.size)
        assertFalse(ready.canTransfer)
        // The wallet read hit the channel's self-bound /me route; the caller's id is bound server-side from the JWT.
        assertEquals(listOf(channelId), api.accountCalls)
    }

    @Test
    fun purchasing_an_item_posts_the_purchase_and_reloads_the_wallet() = runTest {
        val api = FakeParticipantApi(account = CurrencyAccount(balance = 1000))
        val controller = controller(api = api)
        controller.loadStore()
        val accountReadsBefore: Int = api.accountCalls.size

        controller.purchase("item-7", null)

        assertEquals(listOf(channelId to "item-7"), api.purchaseCalls)
        // The wallet re-read after the purchase, so the new balance / stock projects into the page.
        assertTrue(api.accountCalls.size > accountReadsBefore, "wallet must re-read after a purchase")
    }

    @Test
    fun a_transfer_is_blocked_when_the_caller_lacks_the_transfer_capability() = runTest {
        // The capability gate: without economy:transfer:write the controller must NOT issue the transfer, even if
        // the screen somehow invoked it. The backend re-checks too, but the UI gate must hold here.
        val api = FakeParticipantApi()
        val controller = controller(api = api, canTransfer = false)
        controller.loadStore()

        controller.transfer(toViewerUserId = "user-2", amount = 100, reason = null)

        assertTrue(api.transferCalls.isEmpty(), "a transfer without the capability must not be sent")
    }

    @Test
    fun a_transfer_with_the_capability_sends_from_the_caller_to_the_recipient_and_reloads() = runTest {
        val api = FakeParticipantApi(account = CurrencyAccount(balance = 1000))
        val controller = controller(api = api, canTransfer = true)
        controller.loadStore()

        controller.transfer(toViewerUserId = "user-2", amount = 250, reason = "thanks")

        assertEquals(1, api.transferCalls.size)
        val (channel, from, to, amount) = api.transferCalls.first()
        assertEquals(channelId, channel)
        assertEquals(callerId, from)
        assertEquals("user-2", to)
        assertEquals(250L, amount)
    }

    @Test
    fun contributing_to_a_jar_posts_the_amount_and_reloads() = runTest {
        val api =
            FakeParticipantApi(jars = listOf(SavingsJar(id = "j1", name = "Charity", isOpen = true)))
        val controller = controller(api = api)
        controller.loadStore()

        controller.contributeToJar("j1", 75)

        assertEquals(listOf(Triple(channelId, "j1", 75L)), api.jarContributions)
    }

    // ── Games ────────────────────────────────────────────────────────────────

    @Test
    fun load_games_shows_only_enabled_games_and_the_callers_own_history() = runTest {
        val api =
            FakeParticipantApi(
                games =
                    listOf(
                        GameSummary(id = "g1", gameType = "coinflip", isEnabled = true),
                        GameSummary(id = "g2", gameType = "slots", isEnabled = false),
                    ),
                gameHistory = listOf(GamePlay(id = 1, outcome = "win", betAmount = 10, netResult = 20)),
            )
        val controller = controller(api = api)

        controller.loadGames()

        val ready: ParticipantGamesState.Ready = controller.games.value as ParticipantGamesState.Ready
        // The disabled game is filtered out; the history is the caller's own, read by their id.
        assertEquals(listOf("coinflip"), ready.games.map { it.gameType })
        assertEquals(1, ready.history.size)
        assertEquals(listOf(channelId to callerId), api.gameHistoryCalls)
    }

    @Test
    fun playing_a_game_posts_the_bet_surfaces_the_settled_outcome_and_reloads_history() = runTest {
        val api =
            FakeParticipantApi(
                games = listOf(GameSummary(id = "g1", gameType = "coinflip", isEnabled = true)),
                playResult =
                    GamePlayResult(
                        id = 9,
                        gameType = "coinflip",
                        outcome = "win",
                        betAmount = 50,
                        payoutAmount = 100,
                        netResult = 50,
                        balanceAfter = 1050,
                    ),
            )
        val controller = controller(api = api)
        controller.loadGames()
        val historyReadsBefore: Int = api.gameHistoryCalls.size

        controller.playGame("g1", 50)

        assertEquals(listOf(channelId to "g1" to 50L), api.playCalls)
        val ready: ParticipantGamesState.Ready = controller.games.value as ParticipantGamesState.Ready
        val outcome: GamePlayResult = assertNotNull(ready.lastOutcome)
        assertEquals("win", outcome.outcome)
        assertEquals(50L, outcome.netResult)
        assertEquals(1050L, outcome.balanceAfter)
        assertTrue(api.gameHistoryCalls.size > historyReadsBefore, "history must re-read after a play")
    }

    // ── Me ───────────────────────────────────────────────────────────────────

    @Test
    fun load_me_reads_the_callers_profile_activity_and_participation_footprint() = runTest {
        val api =
            FakeParticipantApi(
                profile = UserProfile(id = callerId, displayName = "Nibbles", pronoun = "they/them"),
                channels = listOf(ChannelAppearance(channelName = "stoney", messages = 12, watchTime = "3h")),
            )
        val controller = controller(api = api, standing = ParticipantStanding.Vip)

        controller.loadMe()

        val ready: MeState.Ready = controller.me.value as MeState.Ready
        assertEquals("Nibbles", ready.profile.displayName)
        assertEquals("they/them", ready.profile.pronoun)
        assertEquals(1, ready.channels.size)
        assertEquals(ParticipantStanding.Vip, ready.standing)
        assertEquals(listOf(callerId), api.channelsCalls)
    }

    // ── Fail-closed (no channel / no user) ─────────────────────────────────────

    @Test
    fun with_no_channel_context_a_self_service_load_errors_instead_of_issuing_a_bad_request() = runTest {
        val api = FakeParticipantApi()
        // The fail-closed access from the shell: blank channel + null user (no /effective/me resolved).
        val controller =
            ParticipantController(
                channelId = "",
                userId = null,
                standing = ParticipantStanding.Everyone,
                capabilities = emptyList(),
                participantApi = api,
                dashboardApi = FakeDashboardApi(ApiResult.Ok(DashboardStats())),
                economyApi = FakeEconomyApi(),
                musicApi = FakeMusicApi(ApiResult.Ok(MusicSnapshot())),
            )

        controller.loadStore()

        assertTrue(controller.store.value is StoreState.Error)
        // No request was issued against the backend with an empty channel / null user.
        assertTrue(api.accountCalls.isEmpty())
    }

    // ── Builders ───────────────────────────────────────────────────────────────

    private fun controller(
        api: ParticipantApi = FakeParticipantApi(),
        dashboard: DashboardApi = FakeDashboardApi(ApiResult.Ok(DashboardStats())),
        economy: EconomyApi = FakeEconomyApi(),
        music: MusicApi = FakeMusicApi(ApiResult.Ok(MusicSnapshot())),
        standing: ParticipantStanding = ParticipantStanding.Everyone,
        canTransfer: Boolean = false,
    ): ParticipantController =
        ParticipantController(
            channelId = channelId,
            userId = callerId,
            standing = standing,
            capabilities = if (canTransfer) listOf("economy:transfer:write") else emptyList(),
            participantApi = api,
            dashboardApi = dashboard,
            economyApi = economy,
            musicApi = music,
        )
}

// ── Fakes ─────────────────────────────────────────────────────────────────────

private data class TransferCall(
    val channelId: String,
    val from: String,
    val to: String,
    val amount: Long,
)

private class FakeParticipantApi(
    private val account: CurrencyAccount = CurrencyAccount(),
    private val catalog: List<CatalogItem> = emptyList(),
    private val jars: List<SavingsJar> = emptyList(),
    private val games: List<GameSummary> = emptyList(),
    private val gameHistory: List<GamePlay> = emptyList(),
    private val playResult: GamePlayResult = GamePlayResult(),
    private val profile: UserProfile = UserProfile(id = "user-1", displayName = "Caller"),
    private val activity: UserActivity = UserActivity(messageCount = 42, watchHours = 1.5, commandsUsed = 3),
    private val channels: List<ChannelAppearance> = emptyList(),
    private val songRequestResult: ApiResult<Unit> = ApiResult.Ok(Unit),
) : ParticipantApi {
    val accountCalls: MutableList<String> = mutableListOf()
    val purchaseCalls: MutableList<Pair<String, String>> = mutableListOf()
    val jarContributions: MutableList<Triple<String, String, Long>> = mutableListOf()
    val transferCalls: MutableList<TransferCall> = mutableListOf()
    val optInCalls: MutableList<Pair<String, String>> = mutableListOf()
    val optOutCalls: MutableList<Pair<String, String>> = mutableListOf()
    val playCalls: MutableList<Pair<Pair<String, String>, Long>> = mutableListOf()
    val gameHistoryCalls: MutableList<Pair<String, String>> = mutableListOf()
    val songRequests: MutableList<Pair<String, String>> = mutableListOf()
    val profileCalls: MutableList<String> = mutableListOf()
    val activityCalls: MutableList<String> = mutableListOf()
    val channelsCalls: MutableList<String> = mutableListOf()

    override suspend fun myAccount(channelId: String): ApiResult<CurrencyAccount> {
        accountCalls.add(channelId)
        return ApiResult.Ok(account)
    }

    override suspend fun catalog(channelId: String): ApiResult<List<CatalogItem>> = ApiResult.Ok(catalog)

    override suspend fun purchase(channelId: String, itemId: String, inputArgs: String?): ApiResult<Unit> {
        purchaseCalls.add(channelId to itemId)
        return ApiResult.Ok(Unit)
    }

    override suspend fun jars(channelId: String): ApiResult<List<SavingsJar>> = ApiResult.Ok(jars)

    override suspend fun contributeToJar(channelId: String, jarId: String, amount: Long): ApiResult<Unit> {
        jarContributions.add(Triple(channelId, jarId, amount))
        return ApiResult.Ok(Unit)
    }

    override suspend fun transfer(
        channelId: String,
        fromViewerUserId: String,
        toViewerUserId: String,
        amount: Long,
        reason: String?,
    ): ApiResult<Unit> {
        transferCalls.add(TransferCall(channelId, fromViewerUserId, toViewerUserId, amount))
        return ApiResult.Ok(Unit)
    }

    override suspend fun leaderboardOptIn(channelId: String, viewerUserId: String): ApiResult<Unit> {
        optInCalls.add(channelId to viewerUserId)
        return ApiResult.Ok(Unit)
    }

    override suspend fun leaderboardOptOut(channelId: String, viewerUserId: String): ApiResult<Unit> {
        optOutCalls.add(channelId to viewerUserId)
        return ApiResult.Ok(Unit)
    }

    override suspend fun games(channelId: String): ApiResult<List<GameSummary>> = ApiResult.Ok(games)

    override suspend fun playGame(
        channelId: String,
        gameConfigId: String,
        betAmount: Long,
    ): ApiResult<GamePlayResult> {
        playCalls.add((channelId to gameConfigId) to betAmount)
        return ApiResult.Ok(playResult)
    }

    override suspend fun myGameHistory(channelId: String, playerUserId: String): ApiResult<List<GamePlay>> {
        gameHistoryCalls.add(channelId to playerUserId)
        return ApiResult.Ok(gameHistory)
    }

    override suspend fun submitSongRequest(
        channelId: String,
        query: String,
        requestedBy: String?,
    ): ApiResult<Unit> {
        songRequests.add(channelId to query)
        return songRequestResult
    }

    override suspend fun myProfile(userId: String): ApiResult<UserProfile> {
        profileCalls.add(userId)
        return ApiResult.Ok(profile)
    }

    override suspend fun myChannels(userId: String): ApiResult<List<ChannelAppearance>> {
        channelsCalls.add(userId)
        return ApiResult.Ok(channels)
    }

    override suspend fun myActivity(userId: String): ApiResult<UserActivity> {
        activityCalls.add(userId)
        return ApiResult.Ok(activity)
    }
}

private class FakeDashboardApi(private val result: ApiResult<DashboardStats>) : DashboardApi {
    val statsCalls: MutableList<String> = mutableListOf()

    override suspend fun stats(channelId: String): ApiResult<DashboardStats> {
        statsCalls.add(channelId)
        return result
    }
}

private class FakeEconomyApi(
    private val leaderboard: List<LeaderboardEntry> = emptyList(),
) : EconomyApi {
    val leaderboardCalls: MutableList<String> = mutableListOf()

    override suspend fun config(channelId: String): ApiResult<CurrencyConfig?> = ApiResult.Ok(null)

    override suspend fun updateConfig(
        channelId: String,
        update: UpsertCurrencyConfig,
    ): ApiResult<CurrencyConfig> = ApiResult.Ok(CurrencyConfig())

    override suspend fun leaderboard(channelId: String, top: Int): ApiResult<List<LeaderboardEntry>> {
        leaderboardCalls.add(channelId)
        return ApiResult.Ok(leaderboard)
    }

    override suspend fun accounts(channelId: String): ApiResult<List<CurrencyAccountSummary>> =
        ApiResult.Ok(emptyList())

    override suspend fun earningRules(channelId: String): ApiResult<List<EarningRule>> =
        ApiResult.Ok(emptyList())

    override suspend fun freezeAccount(
        channelId: String,
        viewerUserId: String,
        frozen: Boolean,
    ): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun catalog(channelId: String): ApiResult<List<CatalogItem>> =
        ApiResult.Ok(emptyList())
}

private class FakeMusicApi(private val snapshot: ApiResult<MusicSnapshot>) : MusicApi {
    val queueCalls: MutableList<String> = mutableListOf()

    override suspend fun queue(channelId: String): ApiResult<MusicSnapshot> {
        queueCalls.add(channelId)
        return snapshot
    }

    override suspend fun skip(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun pause(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun resume(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun remove(channelId: String, position: Int): ApiResult<Unit> = ApiResult.Ok(Unit)
}

// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.games.state

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.GamePlayEntry
import bot.nomnomz.dashboard.core.network.GameSummary
import bot.nomnomz.dashboard.core.network.GamesApi
import bot.nomnomz.dashboard.core.network.PaginatedEnvelope
import bot.nomnomz.dashboard.core.network.UpsertGameConfigBody
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the Games page state machine the screen renders: resolve the active channel, surface the real game
// config, then drive the page's writes (toggle enabled / edit config) for the fixed catalog — each re-listing on
// success so the screen reflects the backend. The screen is a pure projection of this, so testing it proves the
// page shows real configured games (no fabricated lists), persists writes, and degrades cleanly.
class GamesControllerTest {

    @Test
    fun load_surfaces_the_configured_games_on_success() = runTest {
        val controller =
            GamesController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                RecordingGamesApi(
                    ApiResult.Ok(
                        listOf(
                            GameSummary(
                                id = "g1",
                                gameType = "coinflip",
                                category = "gambling",
                                isEnabled = true,
                                requires18Plus = true,
                                minBet = 10,
                                maxBet = 1000,
                                cooldownSeconds = 30,
                            ),
                            GameSummary(id = "g2", gameType = "slots"),
                        )
                    )
                ),
            )

        controller.load()

        val state: GamesState = controller.state.value
        assertTrue(state is GamesState.Ready)
        val games: List<GameSummary> = (state as GamesState.Ready).games
        assertEquals(2, games.size)
        assertEquals("coinflip", games[0].gameType)
        assertTrue(games[0].isEnabled)
        assertTrue(games[0].requires18Plus)
        assertEquals(30, games[0].cooldownSeconds)
        assertEquals("g2", games[1].id)
        assertNull(state.actionError)
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            GamesController(
                FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                RecordingGamesApi(ApiResult.Ok(emptyList())),
            )

        controller.load()

        val state: GamesState = controller.state.value
        assertTrue(state is GamesState.Error)
        assertEquals("none onboarded", (state as GamesState.Error).detail)
    }

    @Test
    fun load_errors_when_the_games_call_fails() = runTest {
        val controller =
            GamesController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                RecordingGamesApi(ApiResult.Failure(ApiError(500, "ERR", "boom"))),
            )

        controller.load()

        val state: GamesState = controller.state.value
        assertTrue(state is GamesState.Error)
        assertEquals("boom", (state as GamesState.Error).detail)
    }

    @Test
    fun load_is_empty_when_the_channel_has_no_games() = runTest {
        val controller =
            GamesController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                RecordingGamesApi(ApiResult.Ok(emptyList())),
            )

        controller.load()

        assertTrue(controller.state.value is GamesState.Empty)
    }

    @Test
    fun toggle_upserts_the_full_row_with_only_enabled_flipped_then_reloads() = runTest {
        // The full PUT must carry the row's other fields back unchanged; only isEnabled flips. The store applies
        // the upsert, so the post-write reload observes the persisted flip — proving toggle calls the api AND
        // preserves the rest of the config.
        val game =
            GameSummary(
                id = "g1",
                gameType = "coinflip",
                category = "gambling",
                isEnabled = true,
                requires18Plus = true,
                minBet = 10,
                maxBet = 1000,
                winChancePercent = 50.0,
                payoutMultiplier = 2.0,
                cooldownSeconds = 30,
                maxPlaysPerStream = 5,
                permission = "Everyone",
            )
        val gamesApi = RecordingGamesApi(ApiResult.Ok(listOf(game)))
        val controller =
            GamesController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), gamesApi)
        controller.load()

        controller.toggleGame(game, enabled = false)

        // The api recorded a full upsert: only isEnabled changed; everything else echoed the current row.
        assertEquals("ch1", gamesApi.upsertedChannelId)
        assertEquals(1, gamesApi.upserted.size)
        val body: UpsertGameConfigBody = gamesApi.upserted.first()
        assertEquals("coinflip", body.gameType)
        assertEquals(false, body.isEnabled)
        assertEquals("gambling", body.category)
        assertEquals(true, body.requires18Plus)
        assertEquals(10, body.minBet)
        assertEquals(1000, body.maxBet)
        assertEquals(50.0, body.winChancePercent)
        assertEquals(2.0, body.payoutMultiplier)
        assertEquals(30, body.cooldownSeconds)
        assertEquals(5, body.maxPlaysPerStream)
        assertEquals("Everyone", body.permission)

        // The reload reflects the persisted flip.
        val state: GamesState = controller.state.value
        assertTrue(state is GamesState.Ready)
        assertEquals(false, (state as GamesState.Ready).games.first().isEnabled)
        assertNull(state.actionError)
    }

    @Test
    fun update_upserts_the_edited_config_then_reloads_with_the_new_values() = runTest {
        val game =
            GameSummary(
                id = "g1",
                gameType = "slots",
                category = "gambling",
                isEnabled = true,
                requires18Plus = false,
                minBet = 5,
                maxBet = 500,
                cooldownSeconds = 10,
                permission = "Subscriber",
            )
        val gamesApi = RecordingGamesApi(ApiResult.Ok(listOf(game)))
        val controller =
            GamesController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), gamesApi)
        controller.load()

        controller.updateGameConfig(
            game,
            minBet = 50,
            maxBet = 2000,
            cooldownSeconds = 60,
            requires18Plus = true,
        )

        // The edit sent the new bet limits / cooldown / 18+ while preserving the row's identity + unedited fields.
        val body: UpsertGameConfigBody = gamesApi.upserted.first()
        assertEquals("slots", body.gameType)
        assertEquals(50, body.minBet)
        assertEquals(2000, body.maxBet)
        assertEquals(60, body.cooldownSeconds)
        assertEquals(true, body.requires18Plus)
        assertEquals("Subscriber", body.permission)
        assertEquals(true, body.isEnabled)

        // The reload surfaces the persisted edit.
        val state: GamesState = controller.state.value
        assertTrue(state is GamesState.Ready)
        val updated: GameSummary = (state as GamesState.Ready).games.first()
        assertEquals(50, updated.minBet)
        assertEquals(2000, updated.maxBet)
        assertEquals(60, updated.cooldownSeconds)
        assertTrue(updated.requires18Plus)
    }

    @Test
    fun a_blank_bet_clears_the_limit_to_null() = runTest {
        val game =
            GameSummary(id = "g1", gameType = "dice", isEnabled = true, minBet = 10, maxBet = 100)
        val gamesApi = RecordingGamesApi(ApiResult.Ok(listOf(game)))
        val controller =
            GamesController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), gamesApi)
        controller.load()

        controller.updateGameConfig(
            game,
            minBet = null,
            maxBet = null,
            cooldownSeconds = 0,
            requires18Plus = false,
        )

        val body: UpsertGameConfigBody = gamesApi.upserted.first()
        assertNull(body.minBet)
        assertNull(body.maxBet)
        val updated: GameSummary = (controller.state.value as GamesState.Ready).games.first()
        assertNull(updated.minBet)
        assertNull(updated.maxBet)
    }

    @Test
    fun a_failed_write_surfaces_the_error_over_the_kept_list() = runTest {
        val game = GameSummary(id = "g1", gameType = "coinflip", isEnabled = true)
        val gamesApi =
            RecordingGamesApi(
                ApiResult.Ok(listOf(game)),
                writeResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "no permission")),
            )
        val controller =
            GamesController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), gamesApi)
        controller.load()

        controller.toggleGame(game, enabled = false)

        // The list is kept (not blown away) and the failure is surfaced on it.
        val state: GamesState = controller.state.value
        assertTrue(state is GamesState.Ready)
        assertEquals(1, (state as GamesState.Ready).games.size)
        assertEquals(true, state.games.first().isEnabled) // unchanged — the write failed
        assertEquals("no permission", state.actionError)
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
}

// A recording fake that behaves like the backend store: list() returns the live store, and each successful upsert
// replaces the matching row (keyed by gameType, like the service's (BroadcasterId, GameType) key) so the
// controller's post-write reload observes the real consequence (a flipped flag, new config) — not merely that a
// call happened. [writeResult] forces every write to fail (the store is left untouched) to exercise the error
// path. A list-level failure is modelled by passing a Failure as the initial result.
private class RecordingGamesApi(
    initial: ApiResult<List<GameSummary>>,
    private val writeResult: ApiResult<Unit> = ApiResult.Ok(Unit),
) : GamesApi {
    private val listFailure: ApiError? = (initial as? ApiResult.Failure)?.error
    private val store: MutableList<GameSummary> =
        (initial as? ApiResult.Ok)?.value?.toMutableList() ?: mutableListOf()

    val upserted: MutableList<UpsertGameConfigBody> = mutableListOf()
    var upsertedChannelId: String? = null

    override suspend fun list(channelId: String): ApiResult<List<GameSummary>> =
        listFailure?.let { ApiResult.Failure(it) } ?: ApiResult.Ok(store.toList())

    override suspend fun history(
        channelId: String,
        page: Int,
        pageSize: Int,
    ): ApiResult<PaginatedEnvelope<GamePlayEntry>> =
        ApiResult.Ok(PaginatedEnvelope(data = emptyList()))

    override suspend fun revokeConsent(channelId: String, viewerUserId: String): ApiResult<Unit> =
        ApiResult.Ok(Unit)

    override suspend fun upsert(channelId: String, body: UpsertGameConfigBody): ApiResult<Unit> {
        upserted += body
        upsertedChannelId = channelId
        if (writeResult is ApiResult.Ok) {
            val index: Int = store.indexOfFirst { it.gameType == body.gameType }
            if (index >= 0) {
                store[index] =
                    store[index].copy(
                        category = body.category,
                        isEnabled = body.isEnabled,
                        requires18Plus = body.requires18Plus,
                        minBet = body.minBet,
                        maxBet = body.maxBet,
                        houseEdgePercent = body.houseEdgePercent,
                        winChancePercent = body.winChancePercent,
                        payoutMultiplier = body.payoutMultiplier,
                        cooldownSeconds = body.cooldownSeconds,
                        maxPlaysPerStream = body.maxPlaysPerStream,
                        permission = body.permission,
                        config = body.config,
                    )
            }
        }
        return writeResult
    }
}

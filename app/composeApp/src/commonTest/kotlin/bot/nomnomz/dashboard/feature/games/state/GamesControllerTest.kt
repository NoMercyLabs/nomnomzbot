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
import bot.nomnomz.dashboard.core.network.GameSummary
import bot.nomnomz.dashboard.core.network.GamesApi
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the Games page state machine the screen renders: resolve the active channel, then surface the real game
// config — empty as Empty, a failure of either step as Error. The screen is a pure projection of this, so testing
// it proves the page shows real configured games (no fabricated lists) and degrades cleanly.
class GamesControllerTest {

    @Test
    fun load_surfaces_the_configured_games_on_success() = runTest {
        val controller =
            GamesController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeGamesApi(
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
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            GamesController(
                FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                FakeGamesApi(ApiResult.Ok(emptyList())),
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
                FakeGamesApi(ApiResult.Failure(ApiError(500, "ERR", "boom"))),
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
                FakeGamesApi(ApiResult.Ok(emptyList())),
            )

        controller.load()

        assertTrue(controller.state.value is GamesState.Empty)
    }
}

private class FakeChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result
}

private class FakeGamesApi(private val result: ApiResult<List<GameSummary>>) : GamesApi {
    override suspend fun list(channelId: String): ApiResult<List<GameSummary>> = result
}

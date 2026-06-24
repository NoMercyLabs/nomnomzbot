// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.rewards.state

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.RewardSummary
import bot.nomnomz.dashboard.core.network.RewardsApi
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the Rewards page state machine the screen renders: resolve the active channel, then surface the real
// reward list — empty as Empty, a failure of either step as Error. The screen is a pure projection of this, so
// testing it proves the page shows real rewards (no fabricated lists) and degrades cleanly.
class RewardsControllerTest {

    @Test
    fun load_surfaces_the_rewards_on_success() = runTest {
        val controller =
            RewardsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeRewardsApi(
                    ApiResult.Ok(
                        listOf(
                            RewardSummary(
                                id = "r1",
                                title = "Hydrate!",
                                cost = 500,
                                isEnabled = true,
                            ),
                            RewardSummary(id = "r2", title = "Skip Song", cost = 1000, isEnabled = false),
                        )
                    )
                ),
            )

        controller.load()

        val state: RewardsState = controller.state.value
        assertTrue(state is RewardsState.Ready)
        val rewards: List<RewardSummary> = (state as RewardsState.Ready).rewards
        assertEquals(2, rewards.size)
        assertEquals("Hydrate!", rewards[0].title)
        assertEquals(500, rewards[0].cost)
        assertTrue(rewards[0].isEnabled)
        assertEquals("r2", rewards[1].id)
        assertTrue(!rewards[1].isEnabled)
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            RewardsController(
                FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                FakeRewardsApi(ApiResult.Ok(emptyList())),
            )

        controller.load()

        val state: RewardsState = controller.state.value
        assertTrue(state is RewardsState.Error)
        assertEquals("none onboarded", (state as RewardsState.Error).detail)
    }

    @Test
    fun load_errors_when_the_rewards_call_fails() = runTest {
        val controller =
            RewardsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeRewardsApi(ApiResult.Failure(ApiError(500, "ERR", "boom"))),
            )

        controller.load()

        val state: RewardsState = controller.state.value
        assertTrue(state is RewardsState.Error)
        assertEquals("boom", (state as RewardsState.Error).detail)
    }

    @Test
    fun load_is_empty_when_the_channel_has_no_rewards() = runTest {
        val controller =
            RewardsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeRewardsApi(ApiResult.Ok(emptyList())),
            )

        controller.load()

        assertTrue(controller.state.value is RewardsState.Empty)
    }
}

private class FakeChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result
}

private class FakeRewardsApi(private val result: ApiResult<List<RewardSummary>>) : RewardsApi {
    override suspend fun list(channelId: String): ApiResult<List<RewardSummary>> = result
}

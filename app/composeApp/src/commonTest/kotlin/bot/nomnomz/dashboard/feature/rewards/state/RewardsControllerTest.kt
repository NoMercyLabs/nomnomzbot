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
import bot.nomnomz.dashboard.core.network.CreateRewardBody
import bot.nomnomz.dashboard.core.network.RedemptionSummary
import bot.nomnomz.dashboard.core.network.RewardSummary
import bot.nomnomz.dashboard.core.network.RewardsApi
import bot.nomnomz.dashboard.core.network.UpdateRewardBody
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the Rewards page state machine the screen renders: resolve the active channel, then surface the real
// reward list — empty as Empty, a failure of either step as Error. It also proves the page's writes — create /
// toggle / delete each call the api and re-list, surfacing the real consequence (a new row, a flipped flag, a
// removed row), and a failed write keeps the list and surfaces its reason. The screen is a pure projection of
// this, so testing it proves the page shows real rewards (no fabricated lists) and degrades cleanly.
class RewardsControllerTest {

    @Test
    fun load_surfaces_the_rewards_on_success() = runTest {
        val controller =
            RewardsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                RecordingRewardsApi(
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
    fun load_surfaces_the_pending_redemption_queue_alongside_the_rewards() = runTest {
        val controller =
            RewardsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                RecordingRewardsApi(
                    initial = ApiResult.Ok(listOf(RewardSummary(id = "r1", title = "Hydrate!"))),
                    redemptionQueue =
                        listOf(
                            RedemptionSummary(
                                redemptionId = "x1",
                                rewardTitle = "Hydrate!",
                                userDisplayName = "Buyer",
                                cost = 50,
                                status = "unfulfilled",
                            )
                        ),
                ),
            )

        controller.load()

        val ready: RewardsState.Ready = controller.state.value as RewardsState.Ready
        assertEquals(1, ready.redemptions.size)
        assertEquals("Buyer", ready.redemptions.first().userDisplayName)
        assertEquals("unfulfilled", ready.redemptions.first().status)
        assertEquals(1, ready.rewards.size) // the rewards still load alongside the queue
    }

    @Test
    fun fulfilling_a_redemption_calls_the_api_and_reloads_the_page() = runTest {
        val api =
            RecordingRewardsApi(
                initial = ApiResult.Ok(listOf(RewardSummary(id = "r1", title = "Hydrate!"))),
                redemptionQueue =
                    listOf(
                        RedemptionSummary(
                            redemptionId = "x1",
                            rewardTitle = "Hydrate!",
                            userDisplayName = "Buyer",
                            cost = 50,
                            status = "unfulfilled",
                        )
                    ),
            )
        val controller =
            RewardsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()

        controller.fulfillRedemption("x1")

        assertEquals(listOf("x1"), api.fulfilled) // the api was called for that redemption
        assertTrue(controller.state.value is RewardsState.Ready) // reloaded; page intact
    }

    @Test
    fun refunding_a_redemption_calls_the_api() = runTest {
        val api = RecordingRewardsApi(ApiResult.Ok(listOf(RewardSummary(id = "r1", title = "X"))))
        val controller =
            RewardsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()

        controller.refundRedemption("x9")

        assertEquals(listOf("x9"), api.refunded)
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            RewardsController(
                FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                RecordingRewardsApi(ApiResult.Ok(emptyList())),
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
                RecordingRewardsApi(ApiResult.Failure(ApiError(500, "ERR", "boom"))),
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
                RecordingRewardsApi(ApiResult.Ok(emptyList())),
            )

        controller.load()

        assertTrue(controller.state.value is RewardsState.Empty)
    }

    @Test
    fun create_posts_the_body_then_reloads_with_the_new_reward() = runTest {
        // The fake starts empty; the create appends the new reward to its backing store, so the controller's
        // post-write reload must surface it — proving create actually calls the api AND re-lists.
        val rewardsApi = RecordingRewardsApi(ApiResult.Ok(emptyList()))
        val controller =
            RewardsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), rewardsApi)
        controller.load()
        assertTrue(controller.state.value is RewardsState.Empty)

        controller.createReward(title = "Hydrate!", cost = 500, prompt = "Drink up")

        // The api recorded exactly the body the controller built.
        assertEquals(1, rewardsApi.created.size)
        val body: CreateRewardBody = rewardsApi.created.first()
        assertEquals("ch1", rewardsApi.createdChannelId)
        assertEquals("Hydrate!", body.title)
        assertEquals(500, body.cost)
        assertEquals("Drink up", body.prompt)

        // And the reload surfaced the freshly-created row.
        val state: RewardsState = controller.state.value
        assertTrue(state is RewardsState.Ready)
        val rewards: List<RewardSummary> = (state as RewardsState.Ready).rewards
        assertEquals(1, rewards.size)
        assertEquals("Hydrate!", rewards.first().title)
        assertEquals(500, rewards.first().cost)
        assertNull(state.actionError)
    }

    @Test
    fun update_puts_the_edited_fields_then_reloads() = runTest {
        val rewardsApi =
            RecordingRewardsApi(
                ApiResult.Ok(
                    listOf(RewardSummary(id = "r1", title = "Hydrate!", cost = 500, isEnabled = true))
                )
            )
        val controller =
            RewardsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), rewardsApi)
        controller.load()

        controller.updateReward(
            rewardId = "r1",
            title = "Hydrate Now!",
            cost = 750,
            prompt = "Sip",
            isEnabled = false,
        )

        // The edit is a PUT carrying the new fields, addressed by the reward id.
        assertEquals(1, rewardsApi.updated.size)
        val update: Pair<String, UpdateRewardBody> = rewardsApi.updated.first()
        assertEquals("r1", update.first)
        assertEquals("Hydrate Now!", update.second.title)
        assertEquals(750, update.second.cost)
        assertEquals("Sip", update.second.prompt)
        assertEquals(false, update.second.isEnabled)

        // The reload reflects the persisted edit.
        val state: RewardsState = controller.state.value
        assertTrue(state is RewardsState.Ready)
        val reward: RewardSummary = (state as RewardsState.Ready).rewards.first()
        assertEquals("Hydrate Now!", reward.title)
        assertEquals(750, reward.cost)
        assertTrue(!reward.isEnabled)
    }

    @Test
    fun toggle_puts_only_the_enabled_flag_then_reloads_with_the_flipped_state() = runTest {
        val rewardsApi =
            RecordingRewardsApi(
                ApiResult.Ok(listOf(RewardSummary(id = "r1", title = "Hydrate!", isEnabled = true)))
            )
        val controller =
            RewardsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), rewardsApi)
        controller.load()

        controller.toggleReward(rewardId = "r1", enabled = false)

        // A toggle is a partial PUT carrying only isEnabled.
        assertEquals(1, rewardsApi.updated.size)
        val update: Pair<String, UpdateRewardBody> = rewardsApi.updated.first()
        assertEquals("r1", update.first)
        assertEquals(false, update.second.isEnabled)
        assertNull(update.second.title)
        assertNull(update.second.cost)

        // The reload reflects the persisted flip.
        val state: RewardsState = controller.state.value
        assertTrue(state is RewardsState.Ready)
        assertEquals(false, (state as RewardsState.Ready).rewards.first().isEnabled)
    }

    @Test
    fun delete_removes_the_reward_then_reloads_to_empty() = runTest {
        val rewardsApi =
            RecordingRewardsApi(
                ApiResult.Ok(listOf(RewardSummary(id = "r1", title = "Hydrate!", isEnabled = true)))
            )
        val controller =
            RewardsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), rewardsApi)
        controller.load()
        assertTrue(controller.state.value is RewardsState.Ready)

        controller.deleteReward(rewardId = "r1")

        assertEquals(listOf("r1"), rewardsApi.deleted)
        // The store is now empty, so the post-delete reload lands on Empty — the row is really gone.
        assertTrue(controller.state.value is RewardsState.Empty)
    }

    @Test
    fun a_failed_write_surfaces_the_error_over_the_kept_list() = runTest {
        val rewardsApi =
            RecordingRewardsApi(
                ApiResult.Ok(listOf(RewardSummary(id = "r1", title = "Hydrate!", isEnabled = true))),
                writeResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "no permission")),
            )
        val controller =
            RewardsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), rewardsApi)
        controller.load()

        controller.deleteReward(rewardId = "r1")

        // The list is kept (not blown away) and the failure is surfaced on it.
        val state: RewardsState = controller.state.value
        assertTrue(state is RewardsState.Ready)
        assertEquals(1, (state as RewardsState.Ready).rewards.size)
        assertEquals("no permission", state.actionError)
    }
}

private class FakeChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result
}

// A recording fake that behaves like the backend store: list() returns the live store, and each successful
// write mutates the store so the controller's post-write reload observes the real consequence (a new row, a
// flipped flag, a removed row) — not merely that a call happened. [writeResult] forces every write to fail
// (the store is left untouched) to exercise the error path. A list-level failure is modelled by passing a
// Failure as the initial result.
private class RecordingRewardsApi(
    initial: ApiResult<List<RewardSummary>>,
    private val writeResult: ApiResult<Unit> = ApiResult.Ok(Unit),
    private val redemptionQueue: List<RedemptionSummary> = emptyList(),
) : RewardsApi {
    private val listFailure: ApiError? = (initial as? ApiResult.Failure)?.error
    private val store: MutableList<RewardSummary> =
        (initial as? ApiResult.Ok)?.value?.toMutableList() ?: mutableListOf()

    val created: MutableList<CreateRewardBody> = mutableListOf()
    var createdChannelId: String? = null
    val updated: MutableList<Pair<String, UpdateRewardBody>> = mutableListOf()
    val deleted: MutableList<String> = mutableListOf()

    override suspend fun list(channelId: String): ApiResult<List<RewardSummary>> =
        listFailure?.let { ApiResult.Failure(it) } ?: ApiResult.Ok(store.toList())

    override suspend fun create(channelId: String, body: CreateRewardBody): ApiResult<Unit> {
        created += body
        createdChannelId = channelId
        if (writeResult is ApiResult.Ok) {
            store +=
                RewardSummary(
                    id = "r${store.size + 1}",
                    title = body.title,
                    cost = body.cost,
                    isEnabled = true,
                )
        }
        return writeResult
    }

    override suspend fun update(
        channelId: String,
        rewardId: String,
        body: UpdateRewardBody,
    ): ApiResult<Unit> {
        updated += rewardId to body
        if (writeResult is ApiResult.Ok) {
            val index: Int = store.indexOfFirst { it.id == rewardId }
            if (index >= 0) {
                val existing: RewardSummary = store[index]
                store[index] =
                    existing.copy(
                        title = body.title ?: existing.title,
                        cost = body.cost ?: existing.cost,
                        isEnabled = body.isEnabled ?: existing.isEnabled,
                    )
            }
        }
        return writeResult
    }

    override suspend fun delete(channelId: String, rewardId: String): ApiResult<Unit> {
        deleted += rewardId
        if (writeResult is ApiResult.Ok) {
            store.removeAll { it.id == rewardId }
        }
        return writeResult
    }

    override suspend fun redemptions(
        channelId: String,
        status: String?,
    ): ApiResult<List<RedemptionSummary>> = ApiResult.Ok(redemptionQueue)

    val fulfilled: MutableList<String> = mutableListOf()
    val refunded: MutableList<String> = mutableListOf()

    override suspend fun fulfillRedemption(channelId: String, redemptionId: String): ApiResult<Unit> {
        fulfilled += redemptionId
        return writeResult
    }

    override suspend fun refundRedemption(channelId: String, redemptionId: String): ApiResult<Unit> {
        refunded += redemptionId
        return writeResult
    }

    override suspend fun sync(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
}

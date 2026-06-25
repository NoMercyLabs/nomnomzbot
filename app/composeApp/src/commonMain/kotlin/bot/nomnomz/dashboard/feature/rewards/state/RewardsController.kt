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

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CreateRewardBody
import bot.nomnomz.dashboard.core.network.RedemptionSummary
import bot.nomnomz.dashboard.core.network.RewardSummary
import bot.nomnomz.dashboard.core.network.RewardsApi
import bot.nomnomz.dashboard.core.network.UpdateRewardBody
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Rewards page's state-holder (frontend-ia.md §3 — the channel's channel-point rewards). Resolves the active
// channel, then loads its real reward list from the backend (Twitch Helix Custom Rewards; no fabricated rewards).
// It also drives the page's writes — create / edit / toggle / delete — each of which re-lists on success so the
// screen always reflects the backend's truth. The screen renders [state]; a retry / reconnect calls [load] again.
class RewardsController(
    private val channelsApi: ChannelsApi,
    private val rewardsApi: RewardsApi,
) {
    private val _state: MutableStateFlow<RewardsState> = MutableStateFlow(RewardsState.Loading)

    /** The page render state: loading / ready (with the rewards) / empty / error. */
    val state: StateFlow<RewardsState> = _state.asStateFlow()

    // The channel the writes target — resolved by [load] and reused by every mutation so a write never has to
    // re-resolve the channel. Null until the first successful resolve.
    private var channelId: String? = null

    /** Resolve the active channel, then load its rewards list. */
    suspend fun load() {
        _state.value = RewardsState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = RewardsState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id

        val rewards: List<RewardSummary> =
            when (val result: ApiResult<List<RewardSummary>> = rewardsApi.list(channel.id)) {
                is ApiResult.Failure -> {
                    _state.value = RewardsState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        // The pending redemption queue (status=unfulfilled). A failure here must NOT blank the page — the rewards
        // loaded fine — so it degrades to an empty queue rather than erroring the whole screen.
        val redemptions: List<RedemptionSummary> =
            when (val result: ApiResult<List<RedemptionSummary>> =
                rewardsApi.redemptions(channel.id, status = "unfulfilled")
            ) {
                is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }

        _state.value =
            if (rewards.isEmpty() && redemptions.isEmpty()) RewardsState.Empty
            else RewardsState.Ready(rewards, redemptions)
    }

    /** Create a reward, then reload so the new row appears. Surfaces the error on failure. */
    suspend fun createReward(title: String, cost: Int, prompt: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(rewardsApi.create(channel, CreateRewardBody(title, cost, prompt.ifBlank { null })))
    }

    /**
     * Edit a reward's title / cost / prompt (and enabled flag), addressed by its [rewardId]. Reloads on
     * success. Surfaces the error on failure.
     */
    suspend fun updateReward(
        rewardId: String,
        title: String,
        cost: Int,
        prompt: String,
        isEnabled: Boolean,
    ) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(
            rewardsApi.update(
                channel,
                rewardId,
                UpdateRewardBody(
                    title = title,
                    cost = cost,
                    prompt = prompt.ifBlank { null },
                    isEnabled = isEnabled,
                ),
            )
        )
    }

    /** Flip a reward's enabled flag via the update endpoint (a partial PUT carrying only the flag). Reloads. */
    suspend fun toggleReward(rewardId: String, enabled: Boolean) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(rewardsApi.update(channel, rewardId, UpdateRewardBody(isEnabled = enabled)))
    }

    /** Delete a reward, addressed by its [rewardId]. Reloads on success. Surfaces the error on failure. */
    suspend fun deleteReward(rewardId: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(rewardsApi.delete(channel, rewardId))
    }

    /** Fulfil a queued redemption, then reload so it leaves the pending queue. Surfaces the error on failure. */
    suspend fun fulfillRedemption(redemptionId: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(rewardsApi.fulfillRedemption(channel, redemptionId))
    }

    /** Refund a queued redemption (returns the viewer's points), then reload. Surfaces the error on failure. */
    suspend fun refundRedemption(redemptionId: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(rewardsApi.refundRedemption(channel, redemptionId))
    }

    // A write either reloads the list (success) or surfaces its error over the current Ready list without
    // losing it (failure) — so a failed toggle/delete leaves the page intact with a visible reason.
    private suspend fun afterWrite(result: ApiResult<Unit>) {
        when (result) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    private fun failWrite(detail: String) {
        val current: RewardsState = _state.value
        _state.value =
            if (current is RewardsState.Ready) current.copy(actionError = detail)
            else RewardsState.Error(detail)
    }

    private companion object {
        const val NoChannelError: String = "No active channel — reconnect and try again."
    }
}

/** The Rewards page render state. */
sealed interface RewardsState {
    data object Loading : RewardsState

    /**
     * The channel's rewards are listed, alongside the [redemptions] pending in the queue (status=unfulfilled,
     * newest-first). [actionError] is non-null only when the last create/edit/toggle/delete failed — the screen
     * surfaces it as a transient banner while keeping the list rendered.
     */
    data class Ready(
        val rewards: List<RewardSummary>,
        val redemptions: List<RedemptionSummary> = emptyList(),
        val actionError: String? = null,
    ) : RewardsState

    data object Empty : RewardsState

    data class Error(val detail: String) : RewardsState
}

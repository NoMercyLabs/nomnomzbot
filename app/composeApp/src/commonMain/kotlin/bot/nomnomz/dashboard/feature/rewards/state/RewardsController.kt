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
import bot.nomnomz.dashboard.core.network.EMPTY_PIPELINE_ID
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.PipelinesApi
import bot.nomnomz.dashboard.core.network.RedemptionSummary
import bot.nomnomz.dashboard.core.network.RedemptionTimer
import bot.nomnomz.dashboard.core.network.RewardSummary
import bot.nomnomz.dashboard.core.network.RewardsApi
import bot.nomnomz.dashboard.core.network.UpdateRewardBody
import bot.nomnomz.dashboard.core.realtime.HubEvent
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Rewards page's state-holder (frontend-ia.md §3 — the channel's channel-point rewards). Resolves the active
// channel, then loads its real reward list from the backend (Twitch Helix Custom Rewards; no fabricated rewards).
// It also drives the page's writes — create / edit / toggle / delete — each of which re-lists on success so the
// screen always reflects the backend's truth. The screen renders [state]; a retry / reconnect calls [load] again.
class RewardsController(
    private val channelsApi: ChannelsApi,
    private val rewardsApi: RewardsApi,
    private val pipelinesApi: PipelinesApi,
) {
    private val _state: MutableStateFlow<RewardsState> = MutableStateFlow(RewardsState.Loading)

    /** The page render state: loading / ready (with the rewards) / empty / error. */
    val state: StateFlow<RewardsState> = _state.asStateFlow()

    // The channel the writes target — resolved by [load] and reused by every mutation so a write never has to
    // re-resolve the channel. Null until the first successful resolve.
    private var channelId: String? = null

    /** Resolve the active channel, then load its rewards list. */
    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch after a mutation keeps
        // the current content on screen (no flash) and swaps it when the new data arrives.
        if (_state.value !is RewardsState.Ready) _state.value = RewardsState.Loading

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

        // The channel's pipelines (for the reward form's bind-pipeline picker) and its active/recent redemption
        // countdown timers. Both are supplementary — a failure degrades to empty, never fails the whole page.
        val pipelines: List<PipelineSummary> =
            when (val result: ApiResult<List<PipelineSummary>> = pipelinesApi.list(channel.id)) {
                is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }
        val timers: List<RedemptionTimer> =
            when (val result: ApiResult<List<RedemptionTimer>> = rewardsApi.redemptionTimers(channel.id)) {
                is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }

        _state.value =
            if (rewards.isEmpty() && redemptions.isEmpty() && timers.isEmpty()) RewardsState.Empty
            else RewardsState.Ready(rewards, redemptions, pipelines, timers)
    }

    /**
     * Re-fetch just the redemption timers (not the whole page) — for the live countdown card to refresh without a
     * full reload. Leaves the rest of the Ready state untouched; a failure leaves the current timers in place.
     */
    suspend fun refreshTimers() {
        val channel: String = channelId ?: return
        val current: RewardsState = _state.value
        if (current !is RewardsState.Ready) return
        when (val result: ApiResult<List<RedemptionTimer>> = rewardsApi.redemptionTimers(channel)) {
            is ApiResult.Ok -> _state.value = current.copy(timers = result.value)
            is ApiResult.Failure -> Unit
        }
    }

    /**
     * Create a reward, then reload so the new row appears. [timerDurationSeconds] (0/null = no countdown) starts a
     * countdown on each redemption; [pipelineId] (null = none) binds a pipeline that runs on redemption. Surfaces
     * the error on failure.
     */
    suspend fun createReward(
        title: String,
        cost: Int,
        prompt: String,
        isUserInputRequired: Boolean,
        backgroundColor: String?,
        maxPerStream: Int?,
        maxPerUserPerStream: Int?,
        globalCooldownSeconds: Int?,
        timerDurationSeconds: Int?,
        pipelineId: String?,
    ) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(
            rewardsApi.create(
                channel,
                CreateRewardBody(
                    title = title,
                    cost = cost,
                    prompt = prompt.ifBlank { null },
                    isUserInputRequired = isUserInputRequired,
                    backgroundColor = backgroundColor?.ifBlank { null },
                    maxPerStream = maxPerStream,
                    maxPerUserPerStream = maxPerUserPerStream,
                    globalCooldownSeconds = globalCooldownSeconds,
                    timerDurationSeconds = timerDurationSeconds,
                    pipelineId = pipelineId,
                ),
            )
        )
    }

    /**
     * Edit a reward's title / cost / prompt / enabled flag, plus its countdown [timerDurationSeconds] (0 clears)
     * and bound [pipelineId] (empty string clears), addressed by its [rewardId]. Reloads on success. Surfaces the
     * error on failure.
     */
    suspend fun updateReward(
        rewardId: String,
        title: String,
        cost: Int,
        prompt: String,
        isEnabled: Boolean,
        isPaused: Boolean,
        isUserInputRequired: Boolean,
        backgroundColor: String?,
        maxPerStream: Int?,
        maxPerUserPerStream: Int?,
        globalCooldownSeconds: Int?,
        timerDurationSeconds: Int?,
        pipelineId: String?,
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
                    isPaused = isPaused,
                    isUserInputRequired = isUserInputRequired,
                    backgroundColor = backgroundColor?.ifBlank { null },
                    maxPerStream = maxPerStream,
                    maxPerUserPerStream = maxPerUserPerStream,
                    globalCooldownSeconds = globalCooldownSeconds,
                    timerDurationSeconds = timerDurationSeconds,
                    // Empty sentinel unbinds on "None" (a null is dropped by the serializer and left unchanged).
                    pipelineId = pipelineId ?: EMPTY_PIPELINE_ID,
                ),
            )
        )
    }

    /** Pause a running redemption timer, then refresh the timer list. Surfaces the error on failure. */
    suspend fun pauseTimer(timerId: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterTimerAction(rewardsApi.pauseTimer(channel, timerId))
    }

    /** Resume a paused redemption timer, then refresh the timer list. Surfaces the error on failure. */
    suspend fun resumeTimer(timerId: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterTimerAction(rewardsApi.resumeTimer(channel, timerId))
    }

    /** Complete a redemption timer now (fulfils on Twitch), then refresh. Surfaces the error on failure. */
    suspend fun completeTimer(timerId: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterTimerAction(rewardsApi.completeTimer(channel, timerId))
    }

    /** Cancel a redemption timer (stops counting), then refresh. Surfaces the error on failure. */
    suspend fun cancelTimer(timerId: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterTimerAction(rewardsApi.cancelTimer(channel, timerId))
    }

    // A timer action refreshes just the timer list on success (the rewards/queue are unaffected) or surfaces its
    // error over the current Ready state.
    private suspend fun afterTimerAction(result: ApiResult<Unit>) {
        when (result) {
            is ApiResult.Ok -> refreshTimers()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
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

    /**
     * Trigger a full re-pull from Twitch so the local read model catches up with rewards created or modified
     * directly on the Twitch dashboard. Reloads on success; surfaces the error on failure.
     */
    suspend fun sync() {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(rewardsApi.sync(channel))
    }

    /**
     * Import ALL of the channel's Twitch rewards — including EXTERNAL rewards created outside the bot — into the
     * read model so they become visible (read-only until taken control of). Reloads on success; surfaces the
     * error on failure. Distinct from [sync], which only refreshes the bot's own rewards.
     */
    suspend fun import() {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(rewardsApi.import(channel))
    }

    /**
     * Take control of an external reward by recreating it under the bot's own Twitch client, so the bot can then
     * manage it. Reloads on success (the row flips to manageable); surfaces the error on failure.
     */
    suspend fun recreate(rewardId: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(rewardsApi.recreate(channel, rewardId))
    }

    /**
     * Subscribe to [hubEvents] so new redemptions appear instantly without a poll:
     * - [HubEvent.RewardRedeemed]: prepends a new [RedemptionSummary] to the pending queue (cap 50).
     */
    suspend fun subscribeToHub(hubEvents: SharedFlow<HubEvent>) {
        hubEvents.collect { evt ->
            if (evt !is HubEvent.RewardRedeemed) return@collect
            val current: RewardsState = _state.value
            if (current !is RewardsState.Ready) return@collect
            val newItem: RedemptionSummary = RedemptionSummary(
                redemptionId = evt.event.redemptionId,
                rewardId = evt.event.rewardId,
                rewardTitle = evt.event.rewardTitle,
                userId = evt.event.userId,
                userDisplayName = evt.event.userDisplayName,
                cost = evt.event.cost,
                userInput = evt.event.userInput,
                status = "unfulfilled",
                redeemedAt = evt.event.timestamp,
            )
            _state.value = current.copy(redemptions = (listOf(newItem) + current.redemptions).take(50))
        }
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
        val pipelines: List<PipelineSummary> = emptyList(),
        val timers: List<RedemptionTimer> = emptyList(),
        val actionError: String? = null,
    ) : RewardsState

    data object Empty : RewardsState

    data class Error(val detail: String) : RewardsState
}

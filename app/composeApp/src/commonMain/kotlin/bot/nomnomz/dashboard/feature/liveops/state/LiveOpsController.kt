// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.liveops.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CreatePollBody
import bot.nomnomz.dashboard.core.network.CreatePredictionBody
import bot.nomnomz.dashboard.core.network.LiveOpsAdSchedule
import bot.nomnomz.dashboard.core.network.LiveOpsApi
import bot.nomnomz.dashboard.core.network.LiveOpsClipStub
import bot.nomnomz.dashboard.core.network.LiveOpsMarker
import bot.nomnomz.dashboard.core.network.LiveOpsPoll
import bot.nomnomz.dashboard.core.network.LiveOpsPrediction
import bot.nomnomz.dashboard.core.network.LiveOpsRaid
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// Broadcaster live-ops quick-actions for the Dashboard home page.
// Loads the currently active poll/prediction on demand and exposes fire-and-forget action methods
// for raids, clips, ads and poll/prediction lifecycle.
class LiveOpsController(
    private val channelsApi: ChannelsApi,
    private val liveOpsApi: LiveOpsApi,
) {
    private val _state: MutableStateFlow<LiveOpsState> = MutableStateFlow(LiveOpsState.Idle)
    val state: StateFlow<LiveOpsState> = _state.asStateFlow()

    // Resolved by load(); reused by every mutation.
    private var resolvedChannelId: String? = null

    private val channelId: String? get() = resolvedChannelId

    // Loads the channelId and current active poll/prediction.
    suspend fun load() {
        when (val ch: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
            is ApiResult.Failure -> {
                _state.value = LiveOpsState.Error(ch.error.message)
                return
            }
            is ApiResult.Ok -> {
                val chId: String = ch.value.id
                resolvedChannelId = chId
                val polls: List<LiveOpsPoll> = when (val r: ApiResult<List<LiveOpsPoll>> = liveOpsApi.getPolls(chId)) {
                    is ApiResult.Ok -> r.value
                    is ApiResult.Failure -> emptyList()
                }
                val preds: List<LiveOpsPrediction> = when (val r: ApiResult<List<LiveOpsPrediction>> = liveOpsApi.getPredictions(chId)) {
                    is ApiResult.Ok -> r.value
                    is ApiResult.Failure -> emptyList()
                }
                val schedule: LiveOpsAdSchedule? = when (val r: ApiResult<LiveOpsAdSchedule> = liveOpsApi.getAdSchedule(chId)) {
                    is ApiResult.Ok -> r.value
                    is ApiResult.Failure -> null
                }
                _state.value = LiveOpsState.Ready(
                    channelId = chId,
                    activePoll = polls.firstOrNull { it.status == "ACTIVE" },
                    activePrediction = preds.firstOrNull { it.status == "ACTIVE" || it.status == "LOCKED" },
                    adSchedule = schedule,
                    actionError = null,
                )
            }
        }
    }

    suspend fun createPoll(title: String, choices: List<String>, durationSeconds: Int) {
        val ch: String = channelId ?: return
        val body: CreatePollBody = CreatePollBody(title, choices, durationSeconds)
        when (val r: ApiResult<LiveOpsPoll> = liveOpsApi.createPoll(ch, body)) {
            is ApiResult.Ok -> updateActivePoll(r.value)
            is ApiResult.Failure -> setActionError(r.error.message)
        }
    }

    suspend fun endPoll(status: String) {
        val ch: String = channelId ?: return
        val pollId: String = activePollId() ?: return
        when (val r: ApiResult<LiveOpsPoll> = liveOpsApi.endPoll(ch, pollId, status)) {
            is ApiResult.Ok -> updateActivePoll(if (r.value.status == "ACTIVE") r.value else null)
            is ApiResult.Failure -> setActionError(r.error.message)
        }
    }

    suspend fun createPrediction(title: String, outcomes: List<String>, windowSeconds: Int) {
        val ch: String = channelId ?: return
        val body: CreatePredictionBody = CreatePredictionBody(title, outcomes, windowSeconds)
        when (val r: ApiResult<LiveOpsPrediction> = liveOpsApi.createPrediction(ch, body)) {
            is ApiResult.Ok -> updateActivePrediction(r.value)
            is ApiResult.Failure -> setActionError(r.error.message)
        }
    }

    suspend fun resolvePrediction(winningOutcomeId: String) {
        val ch: String = channelId ?: return
        val predId: String = activePredictionId() ?: return
        when (val r: ApiResult<LiveOpsPrediction> = liveOpsApi.endPrediction(ch, predId, "RESOLVED", winningOutcomeId)) {
            is ApiResult.Ok -> updateActivePrediction(null)
            is ApiResult.Failure -> setActionError(r.error.message)
        }
    }

    suspend fun cancelPrediction() {
        val ch: String = channelId ?: return
        val predId: String = activePredictionId() ?: return
        when (val r: ApiResult<LiveOpsPrediction> = liveOpsApi.endPrediction(ch, predId, "CANCELED", null)) {
            is ApiResult.Ok -> updateActivePrediction(null)
            is ApiResult.Failure -> setActionError(r.error.message)
        }
    }

    suspend fun startRaid(targetBroadcasterId: String): LiveOpsRaid? {
        val ch: String = channelId ?: return null
        return when (val r: ApiResult<LiveOpsRaid> = liveOpsApi.startRaid(ch, targetBroadcasterId)) {
            is ApiResult.Ok -> r.value
            is ApiResult.Failure -> { setActionError(r.error.message); null }
        }
    }

    /** Cancel the pending raid. Returns true on success; a failure surfaces on [LiveOpsState.Ready.actionError]
     * so the UI keeps the Cancel affordance up instead of falsely implying the raid was stopped. */
    suspend fun cancelRaid(): Boolean {
        val ch: String = channelId ?: return false
        return when (val r: ApiResult<Unit> = liveOpsApi.cancelRaid(ch)) {
            is ApiResult.Ok -> true
            is ApiResult.Failure -> {
                setActionError(r.error.message)
                false
            }
        }
    }

    suspend fun createClip(): LiveOpsClipStub? {
        val ch: String = channelId ?: return null
        return when (val r: ApiResult<LiveOpsClipStub> = liveOpsApi.createClip(ch)) {
            is ApiResult.Ok -> r.value
            is ApiResult.Failure -> { setActionError(r.error.message); null }
        }
    }

    /**
     * Drop a stream marker (a VOD bookmark) at the current live position with an optional [description]. Returns
     * the created marker on success, or null on failure (Twitch rejects when the channel isn't live — its error
     * surfaces on the panel). No-ops with no channel.
     */
    suspend fun createMarker(description: String?): LiveOpsMarker? {
        val ch: String = channelId ?: return null
        return when (val r: ApiResult<LiveOpsMarker> = liveOpsApi.createMarker(ch, description?.ifBlank { null })) {
            is ApiResult.Ok -> r.value
            is ApiResult.Failure -> { setActionError(r.error.message); null }
        }
    }

    suspend fun startCommercial(lengthSeconds: Int) {
        val ch: String = channelId ?: return
        when (val r = liveOpsApi.startCommercial(ch, lengthSeconds)) {
            is ApiResult.Ok -> Unit
            is ApiResult.Failure -> setActionError(r.error.message)
        }
    }

    suspend fun snoozeNextAd() {
        val ch: String = channelId ?: return
        when (val r = liveOpsApi.snoozeNextAd(ch)) {
            is ApiResult.Ok -> {
                val current: LiveOpsState = _state.value
                if (current is LiveOpsState.Ready) {
                    _state.value = current.copy(
                        adSchedule = current.adSchedule?.copy(
                            snoozeCount = r.value.snoozeCount,
                            snoozeRefreshAt = r.value.snoozeRefreshAt,
                            nextAdAt = r.value.nextAdAt,
                        ),
                    )
                }
            }
            is ApiResult.Failure -> setActionError(r.error.message)
        }
    }

    fun clearError() {
        val current: LiveOpsState = _state.value
        if (current is LiveOpsState.Ready) _state.value = current.copy(actionError = null)
    }

    private fun activePollId(): String? = (_state.value as? LiveOpsState.Ready)?.activePoll?.id

    private fun activePredictionId(): String? = (_state.value as? LiveOpsState.Ready)?.activePrediction?.id

    private fun updateActivePoll(poll: LiveOpsPoll?) {
        val current: LiveOpsState = _state.value
        if (current is LiveOpsState.Ready) _state.value = current.copy(activePoll = poll)
    }

    private fun updateActivePrediction(prediction: LiveOpsPrediction?) {
        val current: LiveOpsState = _state.value
        if (current is LiveOpsState.Ready) _state.value = current.copy(activePrediction = prediction)
    }

    private fun setActionError(message: String) {
        val current: LiveOpsState = _state.value
        if (current is LiveOpsState.Ready) _state.value = current.copy(actionError = message)
    }
}

sealed interface LiveOpsState {
    data object Idle : LiveOpsState
    data class Error(val detail: String) : LiveOpsState
    data class Ready(
        val channelId: String,
        val activePoll: LiveOpsPoll?,
        val activePrediction: LiveOpsPrediction?,
        val adSchedule: LiveOpsAdSchedule?,
        val actionError: String?,
    ) : LiveOpsState
}

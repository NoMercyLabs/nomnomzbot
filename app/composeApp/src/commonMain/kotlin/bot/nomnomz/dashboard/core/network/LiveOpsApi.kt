// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.network

import kotlinx.serialization.Serializable

// Broadcaster live-ops quick-actions: polls, predictions, raids, ads, and clips.
// All routes live under /api/v1/channels/{channelId}/live-ops — matching LiveOpsController.
interface LiveOpsApi {
    // ─── Polls ──────────────────────────────────────────────────────────────
    suspend fun getPolls(channelId: String): ApiResult<List<LiveOpsPoll>>
    suspend fun createPoll(channelId: String, body: CreatePollBody): ApiResult<LiveOpsPoll>
    suspend fun endPoll(channelId: String, pollId: String, status: String): ApiResult<LiveOpsPoll>

    // ─── Predictions ────────────────────────────────────────────────────────
    suspend fun getPredictions(channelId: String): ApiResult<List<LiveOpsPrediction>>
    suspend fun createPrediction(channelId: String, body: CreatePredictionBody): ApiResult<LiveOpsPrediction>
    suspend fun endPrediction(channelId: String, predictionId: String, status: String, winningOutcomeId: String?): ApiResult<LiveOpsPrediction>

    // ─── Raids ──────────────────────────────────────────────────────────────
    suspend fun startRaid(channelId: String, targetTwitchBroadcasterId: String): ApiResult<LiveOpsRaid>
    suspend fun cancelRaid(channelId: String): ApiResult<Unit>

    // ─── Ads ────────────────────────────────────────────────────────────────
    suspend fun getAdSchedule(channelId: String): ApiResult<LiveOpsAdSchedule>
    suspend fun startCommercial(channelId: String, lengthSeconds: Int): ApiResult<LiveOpsCommercial>
    suspend fun snoozeNextAd(channelId: String): ApiResult<LiveOpsAdSnooze>

    // ─── Clips ──────────────────────────────────────────────────────────────
    suspend fun createClip(channelId: String): ApiResult<LiveOpsClipStub>
}

class RestLiveOpsApi(private val client: ApiClient) : LiveOpsApi {
    override suspend fun getPolls(channelId: String): ApiResult<List<LiveOpsPoll>> =
        client.getEnvelope("api/v1/channels/$channelId/live-ops/polls")

    override suspend fun createPoll(channelId: String, body: CreatePollBody): ApiResult<LiveOpsPoll> =
        client.postEnvelope("api/v1/channels/$channelId/live-ops/polls", body)

    override suspend fun endPoll(channelId: String, pollId: String, status: String): ApiResult<LiveOpsPoll> =
        client.patchEnvelope("api/v1/channels/$channelId/live-ops/polls/$pollId/end", EndPollBody(status))

    override suspend fun getPredictions(channelId: String): ApiResult<List<LiveOpsPrediction>> =
        client.getEnvelope("api/v1/channels/$channelId/live-ops/predictions")

    override suspend fun createPrediction(channelId: String, body: CreatePredictionBody): ApiResult<LiveOpsPrediction> =
        client.postEnvelope("api/v1/channels/$channelId/live-ops/predictions", body)

    override suspend fun endPrediction(channelId: String, predictionId: String, status: String, winningOutcomeId: String?): ApiResult<LiveOpsPrediction> =
        client.patchEnvelope(
            "api/v1/channels/$channelId/live-ops/predictions/$predictionId/end",
            EndPredictionBody(status, winningOutcomeId),
        )

    override suspend fun startRaid(channelId: String, targetTwitchBroadcasterId: String): ApiResult<LiveOpsRaid> =
        client.postEnvelope("api/v1/channels/$channelId/live-ops/raids", StartRaidBody(targetTwitchBroadcasterId))

    override suspend fun cancelRaid(channelId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/live-ops/raids")

    override suspend fun getAdSchedule(channelId: String): ApiResult<LiveOpsAdSchedule> =
        client.getEnvelope("api/v1/channels/$channelId/live-ops/ads/schedule")

    override suspend fun startCommercial(channelId: String, lengthSeconds: Int): ApiResult<LiveOpsCommercial> =
        client.postEnvelope("api/v1/channels/$channelId/live-ops/ads/commercial", StartCommercialBody(lengthSeconds))

    override suspend fun snoozeNextAd(channelId: String): ApiResult<LiveOpsAdSnooze> =
        client.postEnvelope("api/v1/channels/$channelId/live-ops/ads/snooze")

    override suspend fun createClip(channelId: String): ApiResult<LiveOpsClipStub> =
        client.postEnvelope("api/v1/channels/$channelId/live-ops/clips")
}

// ─── DTOs ────────────────────────────────────────────────────────────────────

@Serializable
data class LiveOpsPollChoice(val id: String, val title: String, val votes: Int)

@Serializable
data class LiveOpsPoll(
    val id: String,
    val title: String,
    val choices: List<LiveOpsPollChoice>,
    val status: String,
    val duration: Int,
    val startedAt: String,
    val endedAt: String? = null,
)

@Serializable
data class LiveOpsPredictionOutcome(val id: String, val title: String, val users: Int, val channelPoints: Int)

@Serializable
data class LiveOpsPrediction(
    val id: String,
    val title: String,
    val outcomes: List<LiveOpsPredictionOutcome>,
    val predictionWindow: Int,
    val status: String,
    val winningOutcomeId: String? = null,
    val createdAt: String,
    val endedAt: String? = null,
)

@Serializable
data class LiveOpsRaid(val createdAt: String, val isMature: Boolean)

@Serializable
data class LiveOpsAdSchedule(
    val snoozeCount: Int,
    val snoozeRefreshAt: Int,
    val nextAdAt: Int,
    val duration: Int,
    val lastAdAt: Int,
    val prerollFreeTime: Int,
)

@Serializable
data class LiveOpsCommercial(val length: Int, val message: String, val retryAfter: Int)

@Serializable
data class LiveOpsAdSnooze(val snoozeCount: Int, val snoozeRefreshAt: Int, val nextAdAt: Int)

@Serializable
data class LiveOpsClipStub(val id: String, val editUrl: String)

// ─── Request bodies ──────────────────────────────────────────────────────────

@Serializable
data class CreatePollBody(
    val title: String,
    val choices: List<String>,
    val durationSeconds: Int,
    val channelPointsVotingEnabled: Boolean = false,
    val channelPointsPerVote: Int = 0,
)

@Serializable
data class EndPollBody(val status: String)

@Serializable
data class CreatePredictionBody(
    val title: String,
    val outcomes: List<String>,
    val predictionWindowSeconds: Int,
)

@Serializable
data class EndPredictionBody(val status: String, val winningOutcomeId: String? = null)

@Serializable
data class StartRaidBody(val targetTwitchBroadcasterId: String)

@Serializable
data class StartCommercialBody(val lengthSeconds: Int)

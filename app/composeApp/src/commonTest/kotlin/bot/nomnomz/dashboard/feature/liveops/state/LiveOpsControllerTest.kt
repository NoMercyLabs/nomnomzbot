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

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CreatePollBody
import bot.nomnomz.dashboard.core.network.CreatePredictionBody
import bot.nomnomz.dashboard.core.network.LiveOpsAdSchedule
import bot.nomnomz.dashboard.core.network.LiveOpsApi
import bot.nomnomz.dashboard.core.network.LiveOpsClipStub
import bot.nomnomz.dashboard.core.network.LiveOpsCommercial
import bot.nomnomz.dashboard.core.network.LiveOpsAdSnooze
import bot.nomnomz.dashboard.core.network.LiveOpsMarker
import bot.nomnomz.dashboard.core.network.LiveOpsPoll
import bot.nomnomz.dashboard.core.network.LiveOpsPrediction
import bot.nomnomz.dashboard.core.network.LiveOpsRaid
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the live-ops action a mod fires from the Dashboard home actually follows through. The marker action
// is the one this file covers: it drops a VOD bookmark (a streamer favourite) and must carry the operator's
// description, return the created marker on success, and — when Twitch rejects it (the channel isn't live) —
// return null AND surface the backend's error on the panel rather than silently doing nothing.
class LiveOpsControllerTest {

    @Test
    fun mark_moment_posts_the_description_and_returns_the_created_marker() = runTest {
        val api = FakeLiveOpsApi(markerResult = ApiResult.Ok(LiveOpsMarker(id = "m1", positionSeconds = 120)))
        val controller = LiveOpsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()

        val marker: LiveOpsMarker? = controller.createMarker("big play")

        // The marker hit the API for the resolved channel with the description, and the created marker came back.
        assertEquals(listOf<Pair<String, String?>>("ch1" to "big play"), api.markerCalls)
        assertEquals("m1", marker?.id)
        assertEquals(120, marker?.positionSeconds)
    }

    @Test
    fun mark_moment_returns_null_and_surfaces_the_error_when_twitch_rejects() = runTest {
        val api = FakeLiveOpsApi(markerResult = ApiResult.Failure(ApiError(400, "NOT_LIVE", "Channel is not live.")))
        val controller = LiveOpsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()

        val marker: LiveOpsMarker? = controller.createMarker(null)

        // Failure → null result AND the Twitch error on the Ready state (not a silent no-op).
        assertNull(marker)
        assertEquals("Channel is not live.", (controller.state.value as? LiveOpsState.Ready)?.actionError)
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

// Records the marker calls; every other action is unused by these tests and degrades to an empty/failed result
// (load() reads polls/predictions/ad-schedule resiliently, so a failing ad-schedule is fine).
private class FakeLiveOpsApi(
    private val markerResult: ApiResult<LiveOpsMarker>,
) : LiveOpsApi {
    val markerCalls: MutableList<Pair<String, String?>> = mutableListOf()

    override suspend fun createMarker(channelId: String, description: String?): ApiResult<LiveOpsMarker> {
        markerCalls.add(channelId to description)
        return markerResult
    }

    override suspend fun getPolls(channelId: String): ApiResult<List<LiveOpsPoll>> = ApiResult.Ok(emptyList())

    override suspend fun createPoll(channelId: String, body: CreatePollBody): ApiResult<LiveOpsPoll> =
        ApiResult.Failure(ApiError(0, null, "unused"))

    override suspend fun endPoll(channelId: String, pollId: String, status: String): ApiResult<LiveOpsPoll> =
        ApiResult.Failure(ApiError(0, null, "unused"))

    override suspend fun getPredictions(channelId: String): ApiResult<List<LiveOpsPrediction>> =
        ApiResult.Ok(emptyList())

    override suspend fun createPrediction(
        channelId: String,
        body: CreatePredictionBody,
    ): ApiResult<LiveOpsPrediction> = ApiResult.Failure(ApiError(0, null, "unused"))

    override suspend fun endPrediction(
        channelId: String,
        predictionId: String,
        status: String,
        winningOutcomeId: String?,
    ): ApiResult<LiveOpsPrediction> = ApiResult.Failure(ApiError(0, null, "unused"))

    override suspend fun startRaid(channelId: String, targetTwitchBroadcasterId: String): ApiResult<LiveOpsRaid> =
        ApiResult.Failure(ApiError(0, null, "unused"))

    override suspend fun cancelRaid(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun getAdSchedule(channelId: String): ApiResult<LiveOpsAdSchedule> =
        ApiResult.Failure(ApiError(0, null, "unused"))

    override suspend fun startCommercial(channelId: String, lengthSeconds: Int): ApiResult<LiveOpsCommercial> =
        ApiResult.Failure(ApiError(0, null, "unused"))

    override suspend fun snoozeNextAd(channelId: String): ApiResult<LiveOpsAdSnooze> =
        ApiResult.Failure(ApiError(0, null, "unused"))

    override suspend fun createClip(channelId: String): ApiResult<LiveOpsClipStub> =
        ApiResult.Failure(ApiError(0, null, "unused"))
}

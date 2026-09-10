// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.obs.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.ObsApi
import bot.nomnomz.dashboard.core.network.ObsBridgeSetup
import bot.nomnomz.dashboard.core.network.ObsBridgeStatus
import bot.nomnomz.dashboard.core.network.ObsConnection
import bot.nomnomz.dashboard.core.network.ObsInput
import bot.nomnomz.dashboard.core.network.ObsProbe
import bot.nomnomz.dashboard.core.network.ObsScene
import bot.nomnomz.dashboard.core.network.ObsState
import bot.nomnomz.dashboard.core.network.UpsertObsConnectionBody
import bot.nomnomz.dashboard.core.realtime.HubEvent
import bot.nomnomz.dashboard.core.realtime.HubObsLiveState
import kotlin.test.Test
import kotlin.test.assertFalse
import kotlin.test.assertTrue
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest

// S-PL5a: proves the state-desync fix. Without the connect-time probe (DirectObsTransport) and the
// ObsLiveStateChanged hub push it feeds, a dashboard that (re)connects while OBS is ALREADY streaming/recording
// would keep showing "not live" until some future start/stop event fired — a real streamer's live-control page
// lying about their own session. This proves the client side of that fix: an ObsLiveStateChanged push for the
// active channel makes the controller re-read live state, so the real (already-live) status reaches the page.
class ObsControllerTest {

    @Test
    fun obs_live_state_changed_hub_event_reloads_so_an_already_live_session_shows_up() = runTest {
        val obsApi = RecordingObsApi(initial = ObsState(streaming = false, recording = false))
        val controller = ObsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), obsApi)
        controller.load()

        // Before the push: the page still reflects the stale "not live" read from before OBS's connection
        // carried a pre-existing session.
        val before: ObsLive = (controller.state.value as ObsUiState.Ready).live
        assertFalse(before.state.streaming)
        assertFalse(before.state.recording)

        // The direct OBS WebSocket connection (re)establishes; the connect-time probe finds OBS ALREADY
        // streaming and recording — the exact scenario ObsConnectionEstablishedEvent exists for.
        obsApi.currentState = ObsState(streaming = true, recording = true)
        val hubEvents = MutableSharedFlow<HubEvent>()
        val subscription = launch { controller.subscribeToHub(hubEvents) }
        runCurrent() // let the launched collector subscribe before the shared flow emits
        hubEvents.emit(
            HubEvent.ObsLiveStateChanged(
                HubObsLiveState(broadcasterId = "ch1", streaming = true, recording = true, timestamp = "2026-09-10T00:00:00Z")
            )
        )
        runCurrent() // let the collector's reload run

        val after: ObsLive = (controller.state.value as ObsUiState.Ready).live
        assertTrue(after.state.streaming, "the real (already-live) stream state must reach the page")
        assertTrue(after.state.recording, "the real (already-recording) record state must reach the page")
        subscription.cancel()
    }

    @Test
    fun obs_live_state_changed_for_a_different_channel_is_ignored() = runTest {
        val obsApi = RecordingObsApi(initial = ObsState(streaming = false, recording = false))
        val controller = ObsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), obsApi)
        controller.load()

        // Flip the fake's backing state so a spurious reload would be observable, then push the event for
        // a DIFFERENT channel — the active dashboard session must not adopt another channel's live state.
        obsApi.currentState = ObsState(streaming = true, recording = true)
        val hubEvents = MutableSharedFlow<HubEvent>()
        val subscription = launch { controller.subscribeToHub(hubEvents) }
        runCurrent()
        hubEvents.emit(
            HubEvent.ObsLiveStateChanged(
                HubObsLiveState(broadcasterId = "ch2", streaming = true, recording = true, timestamp = "2026-09-10T00:00:00Z")
            )
        )
        runCurrent()

        val state: ObsLive = (controller.state.value as ObsUiState.Ready).live
        assertFalse(state.state.streaming, "a push for another channel must never reload this one")
        subscription.cancel()
    }
}

private class FakeChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result

    override suspend fun list(): ApiResult<List<ChannelSummary>> = ApiResult.Ok(emptyList())

    override suspend fun join(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun leave(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun reset(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun deleteChannel(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun channelScopes(channelId: String) = error("not used by this test")
    override suspend fun startChannelBotConnect(channelId: String) = error("not used by this test")
    override suspend fun channelBotStatus(channelId: String) = error("not used by this test")
    override suspend fun disconnectChannelBot(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun moderatedChannels(): ApiResult<List<ModeratedChannel>> = ApiResult.Ok(emptyList())
}

/** An [ObsApi] whose [state] read reflects [currentState] live, so a re-read after mutating it proves a reload happened. */
private class RecordingObsApi(initial: ObsState) : ObsApi {
    var currentState: ObsState = initial

    override suspend fun connection(channelId: String): ApiResult<ObsConnection> = ApiResult.Ok(ObsConnection())
    override suspend fun upsertConnection(channelId: String, body: UpsertObsConnectionBody): ApiResult<ObsConnection> =
        error("not used by this test")
    override suspend fun bridgeSetup(channelId: String): ApiResult<ObsBridgeSetup> = ApiResult.Ok(ObsBridgeSetup())
    override suspend fun rotateBridgeToken(channelId: String): ApiResult<ObsBridgeSetup> = error("not used by this test")
    override suspend fun bridgeStatus(channelId: String): ApiResult<ObsBridgeStatus> = ApiResult.Ok(ObsBridgeStatus())
    override suspend fun probe(channelId: String): ApiResult<ObsProbe> = ApiResult.Ok(ObsProbe(connected = true))
    override suspend fun state(channelId: String): ApiResult<ObsState> = ApiResult.Ok(currentState)
    override suspend fun scenes(channelId: String): ApiResult<List<ObsScene>> = ApiResult.Ok(emptyList())
    override suspend fun inputs(channelId: String): ApiResult<List<ObsInput>> = ApiResult.Ok(emptyList())
    override suspend fun switchScene(channelId: String, scene: String): ApiResult<Unit> = error("not used by this test")
    override suspend fun setInputMute(channelId: String, inputName: String, muted: Boolean): ApiResult<Unit> =
        error("not used by this test")
    override suspend fun setInputVolume(channelId: String, inputName: String, volumeDb: Double): ApiResult<Unit> =
        error("not used by this test")
    override suspend fun setStreaming(channelId: String, action: Int): ApiResult<Unit> = error("not used by this test")
    override suspend fun setRecording(channelId: String, action: Int): ApiResult<Unit> = error("not used by this test")
}

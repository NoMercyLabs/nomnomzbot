// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.mediashare.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.MediaShareApi
import bot.nomnomz.dashboard.core.network.MediaShareConfig
import bot.nomnomz.dashboard.core.network.MediaShareRequest
import bot.nomnomz.dashboard.core.network.UpdateMediaShareConfigBody
import bot.nomnomz.dashboard.core.realtime.HubChannelEvent
import bot.nomnomz.dashboard.core.realtime.HubEvent
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.runTest

// Proves the S-OBS-09b fix: a played (or rejected/skipped) clip stops lingering in the moderator queue.
// MediaShareService already moves it out of the active lane server-side and publishes
// MediaSharePlaybackChangedEvent — the two gaps closed here are on the DASHBOARD: (1) the default view showed
// EVERY status forever (the "All" lane), so a played item never left the visible list; (2) nothing reloaded the
// queue on another session's/the overlay's change, only on this session's own next write.
class MediaShareControllerTest {

    @Test
    fun load_defaults_to_the_active_lane_which_excludes_a_played_item() = runTest {
        val pending = MediaShareRequest(id = "1", status = "pending")
        val approved = MediaShareRequest(id = "2", status = "approved")
        val played = MediaShareRequest(id = "3", status = "played")
        val api = FakeMediaShareApi(listOf(listOf(pending, approved, played)))
        val controller = MediaShareController(api)

        controller.load()

        val state: MediaShareUiState = controller.state.value
        assertTrue(state is MediaShareUiState.Ready)
        val ready: MediaShareUiState.Ready = state as MediaShareUiState.Ready
        assertEquals(listOf("1", "2"), ready.queue.map { it.id })
        assertEquals(MediaShareLane.Active, ready.lane)
        // The default read still asks the backend for everything (no server-side "active" filter exists) — the
        // narrowing happens client-side, so the wire call itself carries no status.
        assertEquals(listOf<String?>(null), api.queueCalls)
    }

    @Test
    fun the_played_filter_still_shows_a_played_item_as_history() = runTest {
        val pending = MediaShareRequest(id = "1", status = "pending")
        val played = MediaShareRequest(id = "3", status = "played")
        val api = FakeMediaShareApi(listOf(listOf(pending, played)))
        val controller = MediaShareController(api)

        controller.load()
        controller.setStatusFilter(MediaShareLane.Played)

        val state: MediaShareUiState = controller.state.value
        assertTrue(state is MediaShareUiState.Ready)
        val ready: MediaShareUiState.Ready = state as MediaShareUiState.Ready
        assertEquals(listOf("3"), ready.queue.map { it.id })
        assertEquals(MediaShareLane.Played, ready.lane)
        assertEquals(listOf(null, "played"), api.queueCalls)
    }

    @OptIn(ExperimentalCoroutinesApi::class)
    @Test
    fun subscribe_to_hub_reloads_the_queue_when_a_playback_changed_event_arrives() = runTest {
        // Snapshot A: a pending + an approved item — both active. Snapshot B simulates the pending item having
        // just been approved-then-played (removed) by ANOTHER session: only the approved one remains.
        val snapshotA = listOf(MediaShareRequest(id = "1", status = "pending"), MediaShareRequest(id = "2", status = "approved"))
        val snapshotB = listOf(MediaShareRequest(id = "2", status = "approved"))
        val api = FakeMediaShareApi(sequence = listOf(snapshotA, snapshotB))
        val controller = MediaShareController(api)

        controller.load()
        assertEquals(listOf("1", "2"), (controller.state.value as MediaShareUiState.Ready).queue.map { it.id })

        val events: MutableSharedFlow<HubEvent> = MutableSharedFlow(extraBufferCapacity = 16)
        backgroundScope.launch(UnconfinedTestDispatcher(testScheduler)) { controller.subscribeToHub(events) }

        events.emit(
            HubEvent.ChannelEvent(
                HubChannelEvent(type = "media_share_playback_changed", broadcasterId = "ch1")
            )
        )

        // A real second queue() read happened (the reload), and the state now reflects the fresh snapshot —
        // proving the subscription drives an actual reload, not just an accepted event.
        assertEquals(listOf<String?>(null, null), api.queueCalls)
        val state: MediaShareUiState = controller.state.value
        assertTrue(state is MediaShareUiState.Ready)
        assertEquals(listOf("2"), (state as MediaShareUiState.Ready).queue.map { it.id })
    }

    @OptIn(ExperimentalCoroutinesApi::class)
    @Test
    fun subscribe_to_hub_ignores_an_unrelated_channel_event() = runTest {
        val snapshot = listOf(MediaShareRequest(id = "1", status = "pending"))
        val api = FakeMediaShareApi(listOf(snapshot, snapshot))
        val controller = MediaShareController(api)

        controller.load()

        val events: MutableSharedFlow<HubEvent> = MutableSharedFlow(extraBufferCapacity = 16)
        backgroundScope.launch(UnconfinedTestDispatcher(testScheduler)) { controller.subscribeToHub(events) }

        // A ChannelEvent of a DIFFERENT type (e.g. a follow) must not trigger a Media-Share reload.
        events.emit(HubEvent.ChannelEvent(HubChannelEvent(type = "channel.follow", broadcasterId = "ch1")))

        // Only the initial load ever read the queue — the unrelated event caused no fan-out.
        assertEquals(listOf<String?>(null), api.queueCalls)
    }
}

/** Sequential fake: [sequence] holds one "full backend snapshot" per call (the last entry repeats once the
 * script runs out), and [status] (when non-null) filters that snapshot exactly like the real backend's
 * `?status=` query — so a test can script a snapshot change between a load and a hub-triggered reload. */
private class FakeMediaShareApi(private val sequence: List<List<MediaShareRequest>>) : MediaShareApi {

    val queueCalls: MutableList<String?> = mutableListOf()

    override suspend fun queue(status: String?): ApiResult<List<MediaShareRequest>> {
        val index: Int = minOf(queueCalls.size, sequence.lastIndex)
        val snapshot: List<MediaShareRequest> = sequence[index]
        queueCalls.add(status)
        val filtered: List<MediaShareRequest> = if (status == null) snapshot else snapshot.filter { it.status == status }
        return ApiResult.Ok(filtered)
    }

    override suspend fun next(): ApiResult<MediaShareRequest> = error("stub")

    override suspend fun approve(id: String): ApiResult<MediaShareRequest> = error("stub")

    override suspend fun reject(id: String): ApiResult<MediaShareRequest> = error("stub")

    override suspend fun skip(id: String): ApiResult<MediaShareRequest> = error("stub")

    override suspend fun played(id: String): ApiResult<MediaShareRequest> = error("stub")

    override suspend fun reorder(id: String, position: Int): ApiResult<MediaShareRequest> = error("stub")

    override suspend fun config(): ApiResult<MediaShareConfig> = ApiResult.Ok(MediaShareConfig())

    override suspend fun updateConfig(body: UpdateMediaShareConfigBody): ApiResult<MediaShareConfig> = error("stub")
}

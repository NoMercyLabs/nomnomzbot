// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.songrequests.state

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.MusicConfig
import bot.nomnomz.dashboard.core.network.QueuedSong
import bot.nomnomz.dashboard.core.network.SongRequestsApi
import bot.nomnomz.dashboard.core.network.UpdateMusicConfigBody
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the Song Requests page state machine the screen renders: resolve the active channel, surface the real
// queue (empty as Empty, a failure of either step as Error), and drive the supported playback controls. The
// screen is a pure projection of this, so testing it proves the page shows the real music queue (no fabricated
// tracks), controls it through the real backend routes, reloads on a successful control, and degrades cleanly.
class SongRequestsControllerTest {

    @Test
    fun load_surfaces_the_queue_on_success() = runTest {
        val controller =
            SongRequestsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeSongRequestsApi(
                    ApiResult.Ok(
                        listOf(
                            QueuedSong(
                                position = 0,
                                trackName = "Never Gonna Give You Up",
                                artist = "Rick Astley",
                                durationMs = 213_000,
                                requestedBy = "Stoney_Eagle",
                            ),
                            QueuedSong(position = 1, trackName = "Sandstorm", artist = "Darude"),
                        )
                    )
                ),
            )

        controller.load()

        val state: SongRequestsState = controller.state.value
        assertTrue(state is SongRequestsState.Ready)
        val queue: List<QueuedSong> = (state as SongRequestsState.Ready).queue
        assertEquals(2, queue.size)
        assertEquals("Never Gonna Give You Up", queue[0].trackName)
        assertEquals("Rick Astley", queue[0].artist)
        assertEquals("Stoney_Eagle", queue[0].requestedBy)
        assertEquals(1, queue[1].position)
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            SongRequestsController(
                FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                FakeSongRequestsApi(ApiResult.Ok(emptyList())),
            )

        controller.load()

        val state: SongRequestsState = controller.state.value
        assertTrue(state is SongRequestsState.Error)
        assertEquals("none onboarded", (state as SongRequestsState.Error).detail)
    }

    @Test
    fun load_errors_when_the_queue_call_fails() = runTest {
        val controller =
            SongRequestsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeSongRequestsApi(ApiResult.Failure(ApiError(500, "ERR", "boom"))),
            )

        controller.load()

        val state: SongRequestsState = controller.state.value
        assertTrue(state is SongRequestsState.Error)
        assertEquals("boom", (state as SongRequestsState.Error).detail)
    }

    @Test
    fun load_is_empty_when_the_queue_has_no_songs() = runTest {
        val controller =
            SongRequestsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeSongRequestsApi(ApiResult.Ok(emptyList())),
            )

        controller.load()

        val s: SongRequestsState = controller.state.value
        assertTrue(s is SongRequestsState.Ready)
        assertTrue((s as SongRequestsState.Ready).queue.isEmpty())
    }

    @Test
    fun skip_hits_the_skip_route_then_reloads_the_queue() = runTest {
        val before = listOf(QueuedSong(position = 0, trackName = "A"), QueuedSong(position = 1, trackName = "B"))
        val after = listOf(QueuedSong(position = 0, trackName = "B"))
        val songRequestsApi =
            // First load returns both; after the skip succeeds the reload returns the advanced queue.
            FakeSongRequestsApi(queueResults = listOf(ApiResult.Ok(before), ApiResult.Ok(after)))
        val controller =
            SongRequestsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), songRequestsApi)

        controller.load()
        controller.skip()

        // The skip hit the real route with the resolved channel.
        assertEquals(listOf("ch1"), songRequestsApi.skipCalls)
        // The queue reloaded and now reflects the post-skip state.
        val state: SongRequestsState = controller.state.value
        assertTrue(state is SongRequestsState.Ready)
        assertEquals(listOf("B"), (state as SongRequestsState.Ready).queue.map { it.trackName })
        assertNull(state.actionError)
    }

    @Test
    fun pause_hits_the_pause_route_then_reloads() = runTest {
        val queue = listOf(QueuedSong(position = 0, trackName = "A"))
        val songRequestsApi = FakeSongRequestsApi(queueResults = listOf(ApiResult.Ok(queue)))
        val controller =
            SongRequestsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), songRequestsApi)

        controller.load()
        controller.pause()

        assertEquals(listOf("ch1"), songRequestsApi.pauseCalls)
        // Two queue reads: the initial load plus the reload after the successful pause.
        assertEquals(2, songRequestsApi.queueCalls)
    }

    @Test
    fun resume_hits_the_resume_route_then_reloads() = runTest {
        val queue = listOf(QueuedSong(position = 0, trackName = "A"))
        val songRequestsApi = FakeSongRequestsApi(queueResults = listOf(ApiResult.Ok(queue)))
        val controller =
            SongRequestsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), songRequestsApi)

        controller.load()
        controller.resume()

        assertEquals(listOf("ch1"), songRequestsApi.resumeCalls)
        assertEquals(2, songRequestsApi.queueCalls)
    }

    @Test
    fun remove_deletes_the_position_then_reloads_the_remaining_queue() = runTest {
        val before = listOf(QueuedSong(position = 0, trackName = "A"), QueuedSong(position = 1, trackName = "B"))
        val after = listOf(QueuedSong(position = 0, trackName = "A"))
        val songRequestsApi =
            FakeSongRequestsApi(queueResults = listOf(ApiResult.Ok(before), ApiResult.Ok(after)))
        val controller =
            SongRequestsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), songRequestsApi)

        controller.load()
        controller.remove(1)

        // The remove hit the real route with the resolved channel + the zero-based position.
        assertEquals(listOf("ch1" to 1), songRequestsApi.removeCalls)
        val state: SongRequestsState = controller.state.value
        assertTrue(state is SongRequestsState.Ready)
        assertEquals(listOf("A"), (state as SongRequestsState.Ready).queue.map { it.trackName })
        assertNull(state.actionError)
    }

    @Test
    fun a_failed_control_surfaces_the_error_and_keeps_the_queue() = runTest {
        val queue = listOf(QueuedSong(position = 0, trackName = "A"))
        val songRequestsApi =
            FakeSongRequestsApi(
                queueResults = listOf(ApiResult.Ok(queue)),
                controlResult = ApiResult.Failure(ApiError(503, "UNAVAILABLE", "No active music provider.")),
            )
        val controller =
            SongRequestsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), songRequestsApi)

        controller.load()
        controller.skip()

        assertEquals(listOf("ch1"), songRequestsApi.skipCalls)
        val state: SongRequestsState = controller.state.value
        assertTrue(state is SongRequestsState.Ready)
        // The queue is untouched and the failure is surfaced on the Ready state.
        assertEquals(listOf("A"), (state as SongRequestsState.Ready).queue.map { it.trackName })
        assertEquals("No active music provider.", state.actionError)
        // Only the initial load read the queue; the failed control did not trigger a reload.
        assertEquals(1, songRequestsApi.queueCalls)
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
}

private class FakeSongRequestsApi(
    private val queueResults: List<ApiResult<List<QueuedSong>>>,
    // The default-OK result every control (skip/pause/resume/remove) returns unless a test overrides it.
    private val controlResult: ApiResult<Unit> = ApiResult.Ok(Unit),
) : SongRequestsApi {
    // Single-result convenience for the read-only tests (one queue() result, controls unused).
    constructor(result: ApiResult<List<QueuedSong>>) : this(queueResults = listOf(result))

    var queueCalls: Int = 0
        private set

    val skipCalls: MutableList<String> = mutableListOf()
    val pauseCalls: MutableList<String> = mutableListOf()
    val resumeCalls: MutableList<String> = mutableListOf()
    val removeCalls: MutableList<Pair<String, Int>> = mutableListOf()

    override suspend fun queue(channelId: String): ApiResult<List<QueuedSong>> {
        // Walk through the configured sequence; the last entry repeats once the script runs out.
        val index: Int = minOf(queueCalls, queueResults.lastIndex)
        queueCalls += 1
        return queueResults[index]
    }

    override suspend fun skip(channelId: String): ApiResult<Unit> {
        skipCalls.add(channelId)
        return controlResult
    }

    override suspend fun pause(channelId: String): ApiResult<Unit> {
        pauseCalls.add(channelId)
        return controlResult
    }

    override suspend fun resume(channelId: String): ApiResult<Unit> {
        resumeCalls.add(channelId)
        return controlResult
    }

    override suspend fun remove(channelId: String, position: Int): ApiResult<Unit> {
        removeCalls.add(channelId to position)
        return controlResult
    }

    override suspend fun config(channelId: String): ApiResult<MusicConfig> = ApiResult.Ok(MusicConfig())

    override suspend fun updateConfig(channelId: String, body: UpdateMusicConfigBody): ApiResult<MusicConfig> =
        ApiResult.Ok(MusicConfig())

    override suspend fun srPageToken(channelId: String): ApiResult<String> = ApiResult.Ok("")

    override suspend fun rotateSrPageToken(channelId: String): ApiResult<String> = ApiResult.Ok("")
}

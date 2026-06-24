// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.music.state

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.MusicApi
import bot.nomnomz.dashboard.core.network.MusicSnapshot
import bot.nomnomz.dashboard.core.network.MusicTrack
import bot.nomnomz.dashboard.core.network.NowPlaying
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the Music page state machine the screen renders: resolve the active channel, surface the real
// now-playing track AND the upcoming queue (nothing playing and nothing queued as Empty, a failure of either
// step as Error), and drive the supported playback controls. The screen is a pure projection of this, so
// testing it proves the page shows the real playback (no fabricated tracks), controls it through the real
// backend routes, reloads on a successful control so both halves re-project, and degrades cleanly.
class MusicControllerTest {

    @Test
    fun load_surfaces_the_now_playing_track_and_queue_on_success() = runTest {
        val controller =
            MusicController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeMusicApi(
                    ApiResult.Ok(
                        MusicSnapshot(
                            nowPlaying =
                                NowPlaying(
                                    trackName = "Never Gonna Give You Up",
                                    artist = "Rick Astley",
                                    album = "Whenever You Need Somebody",
                                    durationMs = 213_000,
                                    progressMs = 60_000,
                                    isPlaying = true,
                                    volume = 80,
                                    requestedBy = "Stoney_Eagle",
                                    provider = "spotify",
                                ),
                            queue =
                                listOf(
                                    MusicTrack(
                                        position = 0,
                                        trackName = "Sandstorm",
                                        artist = "Darude",
                                        durationMs = 233_000,
                                        requestedBy = "viewer1",
                                    ),
                                    MusicTrack(position = 1, trackName = "Africa", artist = "Toto"),
                                ),
                        )
                    )
                ),
            )

        controller.load()

        val state: MusicState = controller.state.value
        assertTrue(state is MusicState.Ready)
        val ready: MusicState.Ready = state as MusicState.Ready

        // The now-playing half projects the provider's track verbatim.
        val nowPlaying: NowPlaying = assertNotNull(ready.nowPlaying)
        assertEquals("Never Gonna Give You Up", nowPlaying.trackName)
        assertEquals("Rick Astley", nowPlaying.artist)
        assertEquals(60_000, nowPlaying.progressMs)
        assertEquals(213_000, nowPlaying.durationMs)
        assertTrue(nowPlaying.isPlaying)
        assertEquals("spotify", nowPlaying.provider)
        assertEquals("Stoney_Eagle", nowPlaying.requestedBy)

        // The queue half projects the upcoming tracks in order.
        assertEquals(2, ready.queue.size)
        assertEquals("Sandstorm", ready.queue[0].trackName)
        assertEquals("viewer1", ready.queue[0].requestedBy)
        assertEquals(1, ready.queue[1].position)
        assertNull(ready.actionError)
    }

    @Test
    fun load_is_ready_with_a_now_playing_track_and_an_empty_queue() = runTest {
        // Something is playing but nothing is queued — this is Ready (not Empty), so the now-playing card shows.
        val controller =
            MusicController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeMusicApi(
                    ApiResult.Ok(
                        MusicSnapshot(
                            nowPlaying = NowPlaying(trackName = "Solo Track", isPlaying = true, provider = "youtube"),
                            queue = emptyList(),
                        )
                    )
                ),
            )

        controller.load()

        val state: MusicState = controller.state.value
        assertTrue(state is MusicState.Ready)
        assertEquals("Solo Track", (state as MusicState.Ready).nowPlaying?.trackName)
        assertTrue(state.queue.isEmpty())
    }

    @Test
    fun load_is_empty_when_nothing_is_playing_and_the_queue_is_empty() = runTest {
        val controller =
            MusicController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeMusicApi(ApiResult.Ok(MusicSnapshot(nowPlaying = null, queue = emptyList()))),
            )

        controller.load()

        assertTrue(controller.state.value is MusicState.Empty)
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            MusicController(
                FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                FakeMusicApi(ApiResult.Ok(MusicSnapshot())),
            )

        controller.load()

        val state: MusicState = controller.state.value
        assertTrue(state is MusicState.Error)
        assertEquals("none onboarded", (state as MusicState.Error).detail)
    }

    @Test
    fun load_errors_when_the_snapshot_call_fails() = runTest {
        val controller =
            MusicController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeMusicApi(ApiResult.Failure(ApiError(500, "ERR", "boom"))),
            )

        controller.load()

        val state: MusicState = controller.state.value
        assertTrue(state is MusicState.Error)
        assertEquals("boom", (state as MusicState.Error).detail)
    }

    @Test
    fun pause_hits_the_pause_route_then_reloads_and_reflects_the_paused_state() = runTest {
        val playing =
            MusicSnapshot(nowPlaying = NowPlaying(trackName = "A", isPlaying = true, provider = "spotify"))
        val paused =
            MusicSnapshot(nowPlaying = NowPlaying(trackName = "A", isPlaying = false, provider = "spotify"))
        val musicApi = FakeMusicApi(snapshots = listOf(ApiResult.Ok(playing), ApiResult.Ok(paused)))
        val controller = MusicController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), musicApi)

        controller.load()
        controller.pause()

        // The pause hit the real route with the resolved channel.
        assertEquals(listOf("ch1"), musicApi.pauseCalls)
        // The reload re-projected the now-paused now-playing state.
        val state: MusicState = controller.state.value
        assertTrue(state is MusicState.Ready)
        assertFalse((state as MusicState.Ready).nowPlaying?.isPlaying ?: true)
        assertNull(state.actionError)
        // Two snapshot reads: the initial load plus the reload after the successful pause.
        assertEquals(2, musicApi.queueCalls)
    }

    @Test
    fun resume_hits_the_resume_route_then_reloads() = runTest {
        val snapshot =
            MusicSnapshot(nowPlaying = NowPlaying(trackName = "A", isPlaying = false, provider = "spotify"))
        val musicApi = FakeMusicApi(snapshots = listOf(ApiResult.Ok(snapshot)))
        val controller = MusicController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), musicApi)

        controller.load()
        controller.resume()

        assertEquals(listOf("ch1"), musicApi.resumeCalls)
        assertEquals(2, musicApi.queueCalls)
    }

    @Test
    fun skip_hits_the_skip_route_then_reloads_the_advanced_snapshot() = runTest {
        val before =
            MusicSnapshot(
                nowPlaying = NowPlaying(trackName = "A", isPlaying = true, provider = "spotify"),
                queue = listOf(MusicTrack(position = 0, trackName = "B")),
            )
        val after =
            MusicSnapshot(
                nowPlaying = NowPlaying(trackName = "B", isPlaying = true, provider = "spotify"),
                queue = emptyList(),
            )
        val musicApi = FakeMusicApi(snapshots = listOf(ApiResult.Ok(before), ApiResult.Ok(after)))
        val controller = MusicController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), musicApi)

        controller.load()
        controller.skip()

        assertEquals(listOf("ch1"), musicApi.skipCalls)
        val state: MusicState = controller.state.value
        assertTrue(state is MusicState.Ready)
        // The skipped-to track is now playing and the queue advanced.
        assertEquals("B", (state as MusicState.Ready).nowPlaying?.trackName)
        assertTrue(state.queue.isEmpty())
        assertNull(state.actionError)
    }

    @Test
    fun remove_deletes_the_position_then_reloads_the_remaining_queue() = runTest {
        val before =
            MusicSnapshot(
                nowPlaying = NowPlaying(trackName = "NP", isPlaying = true, provider = "spotify"),
                queue =
                    listOf(
                        MusicTrack(position = 0, trackName = "A"),
                        MusicTrack(position = 1, trackName = "B"),
                    ),
            )
        val after =
            MusicSnapshot(
                nowPlaying = NowPlaying(trackName = "NP", isPlaying = true, provider = "spotify"),
                queue = listOf(MusicTrack(position = 0, trackName = "A")),
            )
        val musicApi = FakeMusicApi(snapshots = listOf(ApiResult.Ok(before), ApiResult.Ok(after)))
        val controller = MusicController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), musicApi)

        controller.load()
        controller.remove(1)

        // The remove hit the real route with the resolved channel + the zero-based position.
        assertEquals(listOf("ch1" to 1), musicApi.removeCalls)
        val state: MusicState = controller.state.value
        assertTrue(state is MusicState.Ready)
        assertEquals(listOf("A"), (state as MusicState.Ready).queue.map { it.trackName })
        assertNull(state.actionError)
    }

    @Test
    fun a_failed_control_surfaces_the_error_and_keeps_the_snapshot() = runTest {
        val snapshot =
            MusicSnapshot(
                nowPlaying = NowPlaying(trackName = "A", isPlaying = true, provider = "spotify"),
                queue = listOf(MusicTrack(position = 0, trackName = "Q1")),
            )
        val musicApi =
            FakeMusicApi(
                snapshots = listOf(ApiResult.Ok(snapshot)),
                controlResult = ApiResult.Failure(ApiError(503, "UNAVAILABLE", "No active music provider.")),
            )
        val controller = MusicController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), musicApi)

        controller.load()
        controller.skip()

        assertEquals(listOf("ch1"), musicApi.skipCalls)
        val state: MusicState = controller.state.value
        assertTrue(state is MusicState.Ready)
        // The snapshot is untouched and the failure is surfaced on the Ready state.
        assertEquals("A", (state as MusicState.Ready).nowPlaying?.trackName)
        assertEquals(listOf("Q1"), state.queue.map { it.trackName })
        assertEquals("No active music provider.", state.actionError)
        // Only the initial load read the snapshot; the failed control did not trigger a reload.
        assertEquals(1, musicApi.queueCalls)
    }
}

private class FakeChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result
}

private class FakeMusicApi(
    private val snapshots: List<ApiResult<MusicSnapshot>>,
    // The default-OK result every control (skip/pause/resume/remove) returns unless a test overrides it.
    private val controlResult: ApiResult<Unit> = ApiResult.Ok(Unit),
) : MusicApi {
    // Single-result convenience for the read-only tests (one queue() result, controls unused).
    constructor(result: ApiResult<MusicSnapshot>) : this(snapshots = listOf(result))

    var queueCalls: Int = 0
        private set

    val skipCalls: MutableList<String> = mutableListOf()
    val pauseCalls: MutableList<String> = mutableListOf()
    val resumeCalls: MutableList<String> = mutableListOf()
    val removeCalls: MutableList<Pair<String, Int>> = mutableListOf()

    override suspend fun queue(channelId: String): ApiResult<MusicSnapshot> {
        // Walk through the configured sequence; the last entry repeats once the script runs out.
        val index: Int = minOf(queueCalls, snapshots.lastIndex)
        queueCalls += 1
        return snapshots[index]
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
}

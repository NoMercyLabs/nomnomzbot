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
import bot.nomnomz.dashboard.core.network.BlockTrackBody
import bot.nomnomz.dashboard.core.network.BlockedTrack
import bot.nomnomz.dashboard.core.network.BlockedTrackPage
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.MusicApi
import bot.nomnomz.dashboard.core.network.MusicConfig
import bot.nomnomz.dashboard.core.network.MusicDevice
import bot.nomnomz.dashboard.core.network.MusicPlaylist
import bot.nomnomz.dashboard.core.network.MusicSnapshot
import bot.nomnomz.dashboard.core.network.MusicSongRequestBody
import bot.nomnomz.dashboard.core.network.MusicTrack
import bot.nomnomz.dashboard.core.network.NowPlaying
import bot.nomnomz.dashboard.core.network.UpdateMusicConfigBody
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

    @Test
    fun load_carries_the_real_shuffle_and_repeat_state_to_the_ready_state() = runTest {
        // The remote controls render the player's actual shuffle/repeat — so the snapshot must carry them
        // through to Ready verbatim (not a fabricated default). This is what lets the Switch show the truth.
        val controller =
            MusicController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeMusicApi(
                    ApiResult.Ok(
                        MusicSnapshot(
                            nowPlaying =
                                NowPlaying(
                                    trackName = "A",
                                    isPlaying = true,
                                    provider = "spotify",
                                    shuffleState = true,
                                    repeatState = "track",
                                )
                        )
                    )
                ),
            )

        controller.load()

        val nowPlaying: NowPlaying =
            assertNotNull((controller.state.value as MusicState.Ready).nowPlaying)
        assertTrue(nowPlaying.shuffleState)
        assertEquals("track", nowPlaying.repeatState)
    }

    @Test
    fun set_shuffle_can_turn_it_off_and_reloads() = runTest {
        // The bug: the button could only ever send shuffle=true. Prove the control can send FALSE (turn it
        // off) and that a successful toggle reloads so the Switch settles on the real post-write state.
        val on =
            MusicSnapshot(
                nowPlaying = NowPlaying(trackName = "A", provider = "spotify", shuffleState = true)
            )
        val off =
            MusicSnapshot(
                nowPlaying = NowPlaying(trackName = "A", provider = "spotify", shuffleState = false)
            )
        val musicApi = FakeMusicApi(snapshots = listOf(ApiResult.Ok(on), ApiResult.Ok(off)))
        val controller = MusicController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), musicApi)
        controller.load()

        controller.setShuffle(false)

        // It sent the OFF value (not a hardcoded true) for the resolved channel, then reloaded.
        assertEquals(listOf("ch1" to false), musicApi.shuffleCalls)
        assertEquals(2, musicApi.queueCalls)
        assertFalse((controller.state.value as MusicState.Ready).nowPlaying?.shuffleState ?: true)
    }

    @Test
    fun set_repeat_sends_the_chosen_mode_and_reloads() = runTest {
        val musicApi =
            FakeMusicApi(
                snapshots =
                    listOf(
                        ApiResult.Ok(
                            MusicSnapshot(nowPlaying = NowPlaying(trackName = "A", provider = "spotify"))
                        )
                    )
            )
        val controller = MusicController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), musicApi)
        controller.load()

        controller.setRepeat("context")

        assertEquals(listOf("ch1" to "context"), musicApi.repeatCalls)
        assertEquals(2, musicApi.queueCalls)
    }

    @Test
    fun load_surfaces_the_blocked_track_page_on_the_ready_state() = runTest {
        val blocked =
            BlockedTrackPage(
                data =
                    listOf(
                        BlockedTrack(
                            id = "bt1",
                            provider = "spotify",
                            trackUri = "spotify:track:abc",
                            title = "Baby Shark",
                            reason = "never again",
                            createdAt = "2026-07-18T12:00:00Z",
                        )
                    ),
                total = 1,
                hasMore = false,
            )
        val musicApi =
            FakeMusicApi(
                snapshots = listOf(ApiResult.Ok(MusicSnapshot(nowPlaying = NowPlaying(trackName = "A")))),
                blockedResult = ApiResult.Ok(blocked),
            )
        val controller = MusicController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), musicApi)

        controller.load()

        val state: MusicState = controller.state.value
        assertTrue(state is MusicState.Ready)
        val ready: MusicState.Ready = state as MusicState.Ready
        // The blocked page projects verbatim: rows, count, and paging signals.
        assertEquals(listOf("Baby Shark"), ready.blockedTracks.map { it.title })
        assertEquals("spotify:track:abc", ready.blockedTracks[0].trackUri)
        assertEquals("never again", ready.blockedTracks[0].reason)
        assertEquals(1, ready.blockedTotal)
        assertEquals(1, ready.blockedPage)
        assertFalse(ready.blockedHasMore)
        assertEquals(listOf(1), musicApi.blockedReads)
    }

    @Test
    fun block_track_posts_the_exact_body_and_rereads_the_blocked_list() = runTest {
        val musicApi = FakeMusicApi(ApiResult.Ok(MusicSnapshot(nowPlaying = NowPlaying(trackName = "A"))))
        val controller = MusicController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), musicApi)
        controller.load()
        musicApi.blockedReads.clear()

        controller.blockTrack(provider = "spotify", trackUri = "spotify:track:abc", title = "Baby Shark", reason = "no")

        // The create carried the exact request body, and the current page re-read so the new row appears.
        assertEquals(
            listOf(BlockTrackBody(provider = "spotify", trackUri = "spotify:track:abc", title = "Baby Shark", reason = "no")),
            musicApi.blockCalls,
        )
        assertEquals(listOf(1), musicApi.blockedReads)
        assertNull((controller.state.value as MusicState.Ready).actionError)
    }

    @Test
    fun a_failed_block_surfaces_the_error_and_keeps_the_rows() = runTest {
        val musicApi =
            FakeMusicApi(
                snapshots = listOf(ApiResult.Ok(MusicSnapshot(nowPlaying = NowPlaying(trackName = "A")))),
                blockResult = ApiResult.Failure(ApiError(409, "TRACK_BLOCKED", "Track is already blocked.")),
            )
        val controller = MusicController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), musicApi)
        controller.load()
        musicApi.blockedReads.clear()

        controller.blockTrack(provider = "spotify", trackUri = "spotify:track:abc", title = "Baby Shark", reason = null)

        val state: MusicState.Ready = controller.state.value as MusicState.Ready
        // The 409 surfaces on the Ready state; nothing re-read, so the rows stay put.
        assertEquals("Track is already blocked.", state.actionError)
        assertTrue(musicApi.blockedReads.isEmpty())
    }

    @Test
    fun unblock_deletes_by_id_and_rereads_the_current_page() = runTest {
        val musicApi = FakeMusicApi(ApiResult.Ok(MusicSnapshot(nowPlaying = NowPlaying(trackName = "A"))))
        val controller = MusicController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), musicApi)
        controller.load()
        musicApi.blockedReads.clear()

        controller.unblockTrack("bt1")

        assertEquals(listOf("bt1"), musicApi.unblockCalls)
        assertEquals(listOf(1), musicApi.blockedReads)
        assertNull((controller.state.value as MusicState.Ready).actionError)
    }

    @Test
    fun load_blocked_tracks_pages_the_list() = runTest {
        val page2 =
            BlockedTrackPage(
                data = listOf(BlockedTrack(id = "bt26", provider = "youtube", trackUri = "yt:v:x", title = "Song 26")),
                total = 26,
                hasMore = false,
            )
        val musicApi =
            FakeMusicApi(
                snapshots = listOf(ApiResult.Ok(MusicSnapshot(nowPlaying = NowPlaying(trackName = "A")))),
                blockedResult = ApiResult.Ok(page2),
            )
        val controller = MusicController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), musicApi)
        controller.load()

        controller.loadBlockedTracks(2)

        val state: MusicState.Ready = controller.state.value as MusicState.Ready
        // The pager read page 2 and the Ready state now carries that page + its position.
        assertEquals(listOf(1, 2), musicApi.blockedReads)
        assertEquals(2, state.blockedPage)
        assertEquals(listOf("Song 26"), state.blockedTracks.map { it.title })
        assertEquals(26, state.blockedTotal)
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

private class FakeMusicApi(
    private val snapshots: List<ApiResult<MusicSnapshot>>,
    // The default-OK result every control (skip/pause/resume/remove) returns unless a test overrides it.
    private val controlResult: ApiResult<Unit> = ApiResult.Ok(Unit),
    // Config defaults to failure so tests that don't configure it don't interfere with Empty state detection.
    private val configResult: ApiResult<MusicConfig> = ApiResult.Failure(ApiError(503, "UNAVAILABLE", "no config")),
    // Blocked-track behavior: the list every read returns, and the configurable mutation results.
    private val blockedResult: ApiResult<BlockedTrackPage> = ApiResult.Ok(BlockedTrackPage()),
    private val blockResult: ApiResult<BlockedTrack> = ApiResult.Ok(BlockedTrack()),
    private val unblockResult: ApiResult<Unit> = ApiResult.Ok(Unit),
) : MusicApi {
    // Single-result convenience for the read-only tests (one queue() result, controls unused).
    constructor(result: ApiResult<MusicSnapshot>) : this(snapshots = listOf(result))

    var queueCalls: Int = 0
        private set

    val skipCalls: MutableList<String> = mutableListOf()
    val pauseCalls: MutableList<String> = mutableListOf()
    val resumeCalls: MutableList<String> = mutableListOf()
    val removeCalls: MutableList<Pair<String, Int>> = mutableListOf()
    val blockedReads: MutableList<Int> = mutableListOf()
    val blockCalls: MutableList<BlockTrackBody> = mutableListOf()
    val unblockCalls: MutableList<String> = mutableListOf()
    val shuffleCalls: MutableList<Pair<String, Boolean>> = mutableListOf()
    val repeatCalls: MutableList<Pair<String, String>> = mutableListOf()

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

    override suspend fun addToQueue(channelId: String, body: MusicSongRequestBody): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun config(channelId: String): ApiResult<MusicConfig> = configResult

    override suspend fun updateConfig(channelId: String, body: UpdateMusicConfigBody): ApiResult<MusicConfig> =
        ApiResult.Ok(MusicConfig())

    override suspend fun srPageToken(channelId: String): ApiResult<String> = ApiResult.Ok("")

    override suspend fun rotateSrPageToken(channelId: String): ApiResult<String> = ApiResult.Ok("")

    override suspend fun seek(channelId: String, positionMs: Int): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun setShuffle(channelId: String, enabled: Boolean): ApiResult<Unit> {
        shuffleCalls.add(channelId to enabled)
        return controlResult
    }

    override suspend fun setRepeat(channelId: String, mode: String): ApiResult<Unit> {
        repeatCalls.add(channelId to mode)
        return controlResult
    }

    override suspend fun transferPlayback(channelId: String, deviceId: String, play: Boolean): ApiResult<Unit> =
        ApiResult.Ok(Unit)

    override suspend fun getDevices(channelId: String): ApiResult<List<MusicDevice>> = ApiResult.Ok(emptyList())

    override suspend fun getPlaylists(channelId: String, offset: Int, limit: Int): ApiResult<List<MusicPlaylist>> =
        ApiResult.Ok(emptyList())

    override suspend fun playContext(channelId: String, contextUri: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun blockedTracks(channelId: String, page: Int, take: Int): ApiResult<BlockedTrackPage> {
        blockedReads.add(page)
        return blockedResult
    }

    override suspend fun blockTrack(channelId: String, body: BlockTrackBody): ApiResult<BlockedTrack> {
        blockCalls.add(body)
        return blockResult
    }

    override suspend fun unblockTrack(channelId: String, blockedTrackId: String): ApiResult<Unit> {
        unblockCalls.add(blockedTrackId)
        return unblockResult
    }
}

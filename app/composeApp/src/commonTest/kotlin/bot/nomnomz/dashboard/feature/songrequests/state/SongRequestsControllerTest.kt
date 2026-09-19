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

import bot.nomnomz.dashboard.core.feedback.FeedbackKind
import bot.nomnomz.dashboard.core.feedback.RecordingFeedback
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BlockTrackBody
import bot.nomnomz.dashboard.core.network.BlockedTrack
import bot.nomnomz.dashboard.core.network.BlockedTrackPage
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.MusicConfig
import bot.nomnomz.dashboard.core.network.MusicSongRequestBody
import bot.nomnomz.dashboard.core.network.QueuedSong
import bot.nomnomz.dashboard.core.network.SongRequestsApi
import bot.nomnomz.dashboard.core.network.UpdateMusicConfigBody
import bot.nomnomz.dashboard.core.realtime.HubEvent
import bot.nomnomz.dashboard.core.realtime.HubMusicState
import bot.nomnomz.dashboard.core.realtime.HubMusicTrack
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertTrue
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.flow.MutableSharedFlow

// Proves the Song Requests page state machine the screen renders: resolve the active channel, surface the
// real queue AND every song-request-specific management capability (config, blocked-track list, SR-page
// share link) — empty queue as Ready with an empty list, a failure as Error. The screen is a pure projection
// of this, so testing it proves the page shows the real music queue (no fabricated tracks), controls it
// through the real backend routes, reloads on a successful control, and degrades cleanly.
//
// S-OBS-04: addToQueue()/blockTrack()/unblockTrack()/loadBlockedTracks() and the shareLink/tokenUrl on load
// moved HERE from MusicController — Music only reads the same backend queue to know what's now playing; this
// controller is the one that owns viewing and mutating the queue's membership and its rules.
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
    }

    @Test
    fun promote_hits_the_promote_route_with_the_position_then_reloads() = runTest {
        val before = listOf(QueuedSong(position = 0, trackName = "A"), QueuedSong(position = 1, trackName = "B"))
        val after = listOf(QueuedSong(position = 0, trackName = "B"), QueuedSong(position = 1, trackName = "A"))
        val songRequestsApi =
            FakeSongRequestsApi(queueResults = listOf(ApiResult.Ok(before), ApiResult.Ok(after)))
        val controller =
            SongRequestsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), songRequestsApi)

        controller.load()
        controller.promote(1)

        // The promote hit the real route with the resolved channel + the zero-based position.
        assertEquals(listOf("ch1" to 1), songRequestsApi.promoteCalls)
        val state: SongRequestsState = controller.state.value
        assertTrue(state is SongRequestsState.Ready)
        assertEquals(listOf("B", "A"), (state as SongRequestsState.Ready).queue.map { it.trackName })
    }

    @Test
    fun ban_hits_the_ban_route_with_the_position_then_reloads() = runTest {
        val before = listOf(QueuedSong(position = 0, trackName = "A"), QueuedSong(position = 1, trackName = "B"))
        val after = listOf(QueuedSong(position = 0, trackName = "A"))
        val songRequestsApi =
            FakeSongRequestsApi(queueResults = listOf(ApiResult.Ok(before), ApiResult.Ok(after)))
        val controller =
            SongRequestsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), songRequestsApi)

        controller.load()
        controller.ban(1)

        // The ban hit the real route with the resolved channel + the zero-based position.
        assertEquals(listOf("ch1" to 1), songRequestsApi.banCalls)
        val state: SongRequestsState = controller.state.value
        assertTrue(state is SongRequestsState.Ready)
        assertEquals(listOf("A"), (state as SongRequestsState.Ready).queue.map { it.trackName })
    }

    @Test
    fun a_failed_ban_announces_on_the_feedback_toast_and_keeps_the_queue() = runTest {
        val queue = listOf(QueuedSong(position = 0, trackName = "A"))
        val songRequestsApi =
            FakeSongRequestsApi(
                queueResults = listOf(ApiResult.Ok(queue)),
                controlResult = ApiResult.Failure(ApiError(500, "ERR", "Ban failed.")),
            )
        val feedback = RecordingFeedback()
        val controller =
            SongRequestsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                songRequestsApi,
                feedback = feedback,
            )

        controller.load()
        controller.ban(0)

        assertEquals(listOf("ch1" to 0), songRequestsApi.banCalls)
        val state: SongRequestsState = controller.state.value
        assertTrue(state is SongRequestsState.Ready)
        assertEquals(listOf("A"), (state as SongRequestsState.Ready).queue.map { it.trackName })
        assertEquals(FeedbackKind.Error, feedback.only.kind)
        assertEquals(listOf("Ban failed."), feedback.only.formatArgs)
        assertEquals(1, songRequestsApi.queueCalls)
    }

    @Test
    fun a_queued_song_with_a_positive_cost_is_flagged_paid_but_a_free_one_is_not() = runTest {
        // Proves the DTO shape the paid badge reads: cost > 0 marks a paid request, 0/default does not.
        val paid = QueuedSong(position = 0, trackName = "A", cost = 500)
        val free = QueuedSong(position = 1, trackName = "B")

        assertTrue(paid.cost > 0)
        assertEquals(0, free.cost)
    }

    @Test
    fun a_failed_control_announces_on_the_feedback_toast_and_keeps_the_queue() = runTest {
        val queue = listOf(QueuedSong(position = 0, trackName = "A"))
        val songRequestsApi =
            FakeSongRequestsApi(
                queueResults = listOf(ApiResult.Ok(queue)),
                controlResult = ApiResult.Failure(ApiError(503, "UNAVAILABLE", "No active music provider.")),
            )
        val feedback = RecordingFeedback()
        val controller =
            SongRequestsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                songRequestsApi,
                feedback = feedback,
            )

        controller.load()
        controller.skip()

        assertEquals(listOf("ch1"), songRequestsApi.skipCalls)
        val state: SongRequestsState = controller.state.value
        assertTrue(state is SongRequestsState.Ready)
        // The queue is untouched and the failure announces on the shell-level feedback toast.
        assertEquals(listOf("A"), (state as SongRequestsState.Ready).queue.map { it.trackName })
        assertEquals(FeedbackKind.Error, feedback.only.kind)
        assertEquals(listOf("No active music provider."), feedback.only.formatArgs)
        // Only the initial load read the queue; the failed control did not trigger a reload.
        assertEquals(1, songRequestsApi.queueCalls)
    }

    @OptIn(ExperimentalCoroutinesApi::class)
    @Test
    fun subscribe_to_hub_reloads_the_queue_when_the_current_track_changes() = runTest {
        val before = listOf(QueuedSong(position = 0, trackName = "A"))
        val after = listOf(QueuedSong(position = 0, trackName = "B"))
        val songRequestsApi =
            FakeSongRequestsApi(queueResults = listOf(ApiResult.Ok(before), ApiResult.Ok(after)))
        val controller =
            SongRequestsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), songRequestsApi)

        controller.load()

        // Collect on an unconfined test dispatcher so the subscription is live immediately and each emission
        // is processed eagerly, matching the pattern used for the same real-time contract elsewhere
        // (ChatController.subscribe_to_hub_skips_a_duplicate_message_id).
        val events: MutableSharedFlow<HubEvent> = MutableSharedFlow(extraBufferCapacity = 16)
        backgroundScope.launch(UnconfinedTestDispatcher(testScheduler)) { controller.subscribeToHub(events) }

        // A real MusicStateChanged push naming a new current track — this is the event
        // PlaybackStateBroadcastHandler sends to the dashboard hub when the queue advances.
        events.emit(HubEvent.MusicStateChanged(HubMusicState(isPlaying = true, currentTrack = HubMusicTrack(trackName = "B"))))

        // The reload actually ran (a second real queue() call) and the state now reflects the fresh queue —
        // proving the subscription drives a real reload, not just an accepted event.
        assertEquals(2, songRequestsApi.queueCalls)
        val state: SongRequestsState = controller.state.value
        assertTrue(state is SongRequestsState.Ready)
        assertEquals(listOf("B"), (state as SongRequestsState.Ready).queue.map { it.trackName })
    }

    @OptIn(ExperimentalCoroutinesApi::class)
    @Test
    fun subscribe_to_hub_skips_reload_when_the_current_track_is_unchanged() = runTest {
        val queue = listOf(QueuedSong(position = 0, trackName = "A"))
        val songRequestsApi = FakeSongRequestsApi(queueResults = listOf(ApiResult.Ok(queue)))
        val controller =
            SongRequestsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), songRequestsApi)

        controller.load()

        val events: MutableSharedFlow<HubEvent> = MutableSharedFlow(extraBufferCapacity = 16)
        backgroundScope.launch(UnconfinedTestDispatcher(testScheduler)) { controller.subscribeToHub(events) }

        // A play/pause toggle on the SAME track does not advance the queue, so it must not trigger a reload.
        events.emit(HubEvent.MusicStateChanged(HubMusicState(isPlaying = false, currentTrack = null)))
        events.emit(HubEvent.MusicStateChanged(HubMusicState(isPlaying = false, currentTrack = null)))

        // Only the initial load ever read the queue — no fan-out from the redundant pushes.
        assertEquals(1, songRequestsApi.queueCalls)
    }

    // S067f/S-OBS-04 — the dashboard must hand the streamer a working, copyable `/sr/{token}` URL, not a bare
    // token string. Proves the displayed URL is built from the REAL resolved backend origin plus the REAL
    // token and that a successful rotate replaces it with a URL carrying the new token.
    @Test
    fun load_builds_the_token_url_from_the_real_origin_and_token_and_rotate_updates_it() = runTest {
        val songRequestsApi =
            FakeSongRequestsApi(
                queueResults = listOf(ApiResult.Ok(emptyList())),
                srPageTokenResult = ApiResult.Ok("abc123"),
                rotateSrPageTokenResult = ApiResult.Ok("newtoken456"),
            )
        val controller =
            SongRequestsController(
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1", login = "streamerlogin"))),
                songRequestsApi = songRequestsApi,
                baseUrlProvider = { "https://dev.nomnomz.bot" },
            )

        controller.load()

        val ready: SongRequestsState.Ready = assertNotNull(controller.state.value as? SongRequestsState.Ready)
        assertEquals("abc123", ready.srPageToken)
        assertEquals("https://dev.nomnomz.bot/sr/abc123", ready.tokenUrl)
        assertEquals("https://dev.nomnomz.bot/sr/@streamerlogin", ready.shareLink)

        controller.rotateSrPageToken()

        val afterRotate: SongRequestsState.Ready = assertNotNull(controller.state.value as? SongRequestsState.Ready)
        assertEquals("newtoken456", afterRotate.srPageToken)
        assertEquals("https://dev.nomnomz.bot/sr/newtoken456", afterRotate.tokenUrl)
    }

    @Test
    fun add_to_queue_posts_the_query_and_requester_then_reloads() = runTest {
        val before = emptyList<QueuedSong>()
        val after = listOf(QueuedSong(position = 0, trackName = "Sandstorm", requestedBy = "viewer1"))
        val songRequestsApi = FakeSongRequestsApi(queueResults = listOf(ApiResult.Ok(before), ApiResult.Ok(after)))
        val controller =
            SongRequestsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), songRequestsApi)

        controller.load()
        controller.addToQueue("Sandstorm", "viewer1")

        assertEquals(listOf("ch1" to MusicSongRequestBody("Sandstorm", "viewer1")), songRequestsApi.addToQueueCalls)
        val state: SongRequestsState = controller.state.value
        assertTrue(state is SongRequestsState.Ready)
        assertEquals(listOf("Sandstorm"), (state as SongRequestsState.Ready).queue.map { it.trackName })
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
        val songRequestsApi =
            FakeSongRequestsApi(queueResults = listOf(ApiResult.Ok(emptyList())), blockedResult = ApiResult.Ok(blocked))
        val controller =
            SongRequestsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), songRequestsApi)

        controller.load()

        val ready: SongRequestsState.Ready = assertNotNull(controller.state.value as? SongRequestsState.Ready)
        assertEquals(listOf("Baby Shark"), ready.blockedTracks.map { it.title })
        assertEquals("spotify:track:abc", ready.blockedTracks[0].trackUri)
        assertEquals(1, ready.blockedTotal)
        assertEquals(1, ready.blockedPage)
        assertEquals(listOf(1), songRequestsApi.blockedReads)
    }

    @Test
    fun block_track_posts_the_exact_body_and_rereads_the_blocked_list() = runTest {
        val songRequestsApi = FakeSongRequestsApi(ApiResult.Ok(emptyList()))
        val controller =
            SongRequestsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), songRequestsApi)
        controller.load()
        songRequestsApi.blockedReads.clear()

        controller.blockTrack(provider = "spotify", trackUri = "spotify:track:abc", title = "Baby Shark", reason = "no")

        assertEquals(
            listOf(BlockTrackBody(provider = "spotify", trackUri = "spotify:track:abc", title = "Baby Shark", reason = "no")),
            songRequestsApi.blockCalls,
        )
        assertEquals(listOf(1), songRequestsApi.blockedReads)
    }

    @Test
    fun unblock_deletes_by_id_and_rereads_the_current_page() = runTest {
        val songRequestsApi = FakeSongRequestsApi(ApiResult.Ok(emptyList()))
        val controller =
            SongRequestsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), songRequestsApi)
        controller.load()
        songRequestsApi.blockedReads.clear()

        controller.unblockTrack("bt1")

        assertEquals(listOf("bt1"), songRequestsApi.unblockCalls)
        assertEquals(listOf(1), songRequestsApi.blockedReads)
    }

    @Test
    fun load_blocked_tracks_pages_the_list() = runTest {
        val page2 =
            BlockedTrackPage(
                data = listOf(BlockedTrack(id = "bt26", provider = "youtube", trackUri = "yt:v:x", title = "Song 26")),
                total = 26,
                hasMore = false,
            )
        val songRequestsApi =
            FakeSongRequestsApi(queueResults = listOf(ApiResult.Ok(emptyList())), blockedResult = ApiResult.Ok(page2))
        val controller =
            SongRequestsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), songRequestsApi)
        controller.load()

        controller.loadBlockedTracks(2)

        val state: SongRequestsState.Ready = controller.state.value as SongRequestsState.Ready
        assertEquals(listOf(1, 2), songRequestsApi.blockedReads)
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

private class FakeSongRequestsApi(
    private val queueResults: List<ApiResult<List<QueuedSong>>>,
    // The default-OK result every control (skip/pause/resume/remove) returns unless a test overrides it.
    private val controlResult: ApiResult<Unit> = ApiResult.Ok(Unit),
    private val srPageTokenResult: ApiResult<String> = ApiResult.Ok(""),
    private val rotateSrPageTokenResult: ApiResult<String> = ApiResult.Ok(""),
    private val blockedResult: ApiResult<BlockedTrackPage> = ApiResult.Ok(BlockedTrackPage()),
    private val blockResult: ApiResult<BlockedTrack> = ApiResult.Ok(BlockedTrack()),
    private val unblockResult: ApiResult<Unit> = ApiResult.Ok(Unit),
) : SongRequestsApi {
    // Single-result convenience for the read-only tests (one queue() result, controls unused).
    constructor(result: ApiResult<List<QueuedSong>>) : this(queueResults = listOf(result))

    var queueCalls: Int = 0
        private set

    val skipCalls: MutableList<String> = mutableListOf()
    val pauseCalls: MutableList<String> = mutableListOf()
    val resumeCalls: MutableList<String> = mutableListOf()
    val removeCalls: MutableList<Pair<String, Int>> = mutableListOf()
    val promoteCalls: MutableList<Pair<String, Int>> = mutableListOf()
    val banCalls: MutableList<Pair<String, Int>> = mutableListOf()
    val addToQueueCalls: MutableList<Pair<String, MusicSongRequestBody>> = mutableListOf()
    val blockedReads: MutableList<Int> = mutableListOf()
    val blockCalls: MutableList<BlockTrackBody> = mutableListOf()
    val unblockCalls: MutableList<String> = mutableListOf()

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

    override suspend fun addToQueue(channelId: String, body: MusicSongRequestBody): ApiResult<Unit> {
        addToQueueCalls.add(channelId to body)
        return controlResult
    }

    override suspend fun promote(channelId: String, position: Int): ApiResult<Unit> {
        promoteCalls.add(channelId to position)
        return controlResult
    }

    override suspend fun ban(channelId: String, position: Int): ApiResult<Unit> {
        banCalls.add(channelId to position)
        return controlResult
    }

    override suspend fun config(channelId: String): ApiResult<MusicConfig> = ApiResult.Ok(MusicConfig())

    override suspend fun updateConfig(channelId: String, body: UpdateMusicConfigBody): ApiResult<MusicConfig> =
        ApiResult.Ok(MusicConfig())

    override suspend fun srPageToken(channelId: String): ApiResult<String> = srPageTokenResult

    override suspend fun rotateSrPageToken(channelId: String): ApiResult<String> = rotateSrPageTokenResult

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

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
import bot.nomnomz.dashboard.core.network.QueuedSong
import bot.nomnomz.dashboard.core.network.SongRequestsApi
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the Song Requests page state machine the screen renders: resolve the active channel, then surface the
// real queue — empty as Empty, a failure of either step as Error. The screen is a pure projection of this, so
// testing it proves the page shows the real music queue (no fabricated tracks) and degrades cleanly.
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

        assertTrue(controller.state.value is SongRequestsState.Empty)
    }
}

private class FakeChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result
}

private class FakeSongRequestsApi(private val result: ApiResult<List<QueuedSong>>) :
    SongRequestsApi {
    override suspend fun queue(channelId: String): ApiResult<List<QueuedSong>> = result
}

// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.timers.state

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.TimerSummary
import bot.nomnomz.dashboard.core.network.TimersApi
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the Timers page state machine the screen renders: resolve the active channel, then surface the real
// scheduled timers — Empty when there are none, or an Error if either step fails. The screen is a pure
// projection of this, so testing it proves the page shows real rows (no fabricated timers) and degrades
// cleanly.
class TimersControllerTest {

    @Test
    fun load_surfaces_the_channels_timers_on_success() = runTest {
        val controller =
            TimersController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeTimersApi(
                    ApiResult.Ok(
                        listOf(
                            TimerSummary(
                                id = 7,
                                name = "Follow reminder",
                                intervalMinutes = 10,
                                isEnabled = true,
                                messageCount = 3,
                            )
                        )
                    )
                ),
            )

        controller.load()

        val state: TimersState = controller.state.value
        assertTrue(state is TimersState.Ready)
        val timers: List<TimerSummary> = (state as TimersState.Ready).timers
        assertEquals(1, timers.size)
        val timer: TimerSummary = timers.first()
        assertEquals(7, timer.id)
        assertEquals("Follow reminder", timer.name)
        assertEquals(10, timer.intervalMinutes)
        assertEquals(true, timer.isEnabled)
        assertEquals(3, timer.messageCount)
    }

    @Test
    fun load_reports_empty_when_the_channel_has_no_timers() = runTest {
        val controller =
            TimersController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeTimersApi(ApiResult.Ok(emptyList())),
            )

        controller.load()

        assertTrue(controller.state.value is TimersState.Empty)
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            TimersController(
                FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                FakeTimersApi(ApiResult.Ok(emptyList())),
            )

        controller.load()

        assertTrue(controller.state.value is TimersState.Error)
    }

    @Test
    fun load_errors_when_the_timers_call_fails() = runTest {
        val controller =
            TimersController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeTimersApi(ApiResult.Failure(ApiError(500, "ERR", "boom"))),
            )

        controller.load()

        assertTrue(controller.state.value is TimersState.Error)
    }
}

private class FakeChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result
}

private class FakeTimersApi(private val result: ApiResult<List<TimerSummary>>) : TimersApi {
    override suspend fun list(channelId: String): ApiResult<List<TimerSummary>> = result
}

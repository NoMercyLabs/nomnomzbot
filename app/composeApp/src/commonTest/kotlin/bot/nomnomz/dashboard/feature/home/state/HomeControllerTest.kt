// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.home.state

import bot.nomnomz.dashboard.core.network.ActivityEvent
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.DashboardApi
import bot.nomnomz.dashboard.core.network.DashboardStats
import bot.nomnomz.dashboard.core.network.StreamApi
import bot.nomnomz.dashboard.core.network.StreamInfo
import bot.nomnomz.dashboard.core.network.StreamInfoUpdate
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the Home page state machine the screen renders: resolve the active channel, then surface the live
// snapshot — or an error if either step fails. The screen is a pure projection of this, so testing it proves
// the page shows real data (no fabricated counts) and degrades cleanly.
class HomeControllerTest {

    @Test
    fun load_surfaces_the_live_channel_snapshot_on_success() = runTest {
        val controller =
            HomeController(
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                dashboardApi = FakeDashboardApi(
                    ApiResult.Ok(
                        DashboardStats(
                            isLive = true,
                            streamTitle = "Live now",
                            viewerCount = 42,
                            followerCount = 1000,
                            uptime = 3720,
                        )
                    )
                ),
                streamApi = FakeStreamApi(),
            )

        controller.load()

        val state: HomeState = controller.state.value
        assertTrue(state is HomeState.Ready)
        val stats: DashboardStats = (state as HomeState.Ready).stats
        assertEquals(true, stats.isLive)
        assertEquals(42, stats.viewerCount)
        assertEquals(1000, stats.followerCount)
        assertEquals("Live now", stats.streamTitle)
        assertEquals(3720, stats.uptime)
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            HomeController(
                channelsApi = FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                dashboardApi = FakeDashboardApi(ApiResult.Ok(DashboardStats())),
                streamApi = FakeStreamApi(),
            )

        controller.load()

        assertTrue(controller.state.value is HomeState.Error)
    }

    @Test
    fun load_errors_when_the_stats_call_fails() = runTest {
        val controller =
            HomeController(
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                dashboardApi = FakeDashboardApi(ApiResult.Failure(ApiError(500, "ERR", "boom"))),
                streamApi = FakeStreamApi(),
            )

        controller.load()

        assertTrue(controller.state.value is HomeState.Error)
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

private class FakeDashboardApi(private val result: ApiResult<DashboardStats>) : DashboardApi {
    override suspend fun stats(channelId: String): ApiResult<DashboardStats> = result
    override suspend fun activity(channelId: String): ApiResult<List<ActivityEvent>> =
        ApiResult.Ok(emptyList())
}

private class FakeStreamApi : StreamApi {
    override suspend fun info(channelId: String): ApiResult<StreamInfo> =
        ApiResult.Ok(StreamInfo())
    override suspend fun update(channelId: String, update: StreamInfoUpdate): ApiResult<StreamInfo> =
        ApiResult.Ok(StreamInfo())
}

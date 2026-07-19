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
import bot.nomnomz.dashboard.core.network.CommandSummary
import bot.nomnomz.dashboard.core.network.CommandsApi
import bot.nomnomz.dashboard.core.network.CommunityApi
import bot.nomnomz.dashboard.core.network.CreateCommandBody
import bot.nomnomz.dashboard.core.network.Category
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.DashboardApi
import bot.nomnomz.dashboard.core.network.DashboardStats
import bot.nomnomz.dashboard.core.network.StreamApi
import bot.nomnomz.dashboard.core.network.StreamInfo
import bot.nomnomz.dashboard.core.network.StreamInfoUpdate
import bot.nomnomz.dashboard.core.network.ViewerOption
import bot.nomnomz.dashboard.core.network.UpdateCommandBody
import bot.nomnomz.dashboard.core.realtime.HubEvent
import bot.nomnomz.dashboard.core.realtime.HubStreamInfoChanged
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.runTest

// Proves the Home page state machine the screen renders: resolve the active channel, then surface the live
// snapshot — or an error if either step fails. The screen is a pure projection of this, so testing it proves
// the page shows real data (no fabricated counts) and degrades cleanly.
@OptIn(ExperimentalCoroutinesApi::class)
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
                commandsApi = FakeCommandsApi(),
                communityApi = FakeCommunityApi(),
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
                commandsApi = FakeCommandsApi(),
                communityApi = FakeCommunityApi(),
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
                commandsApi = FakeCommandsApi(),
                communityApi = FakeCommunityApi(),
            )

        controller.load()

        assertTrue(controller.state.value is HomeState.Error)
    }

    @Test
    fun load_surfaces_top_5_commands_sorted_by_use_count() = runTest {
        val commands: List<CommandSummary> = listOf(
            CommandSummary(name = "!c", useCount = 1),
            CommandSummary(name = "!a", useCount = 50),
            CommandSummary(name = "!b", useCount = 30),
            CommandSummary(name = "!d", useCount = 5),
            CommandSummary(name = "!e", useCount = 20),
            CommandSummary(name = "!f", useCount = 100),
        )
        val controller =
            HomeController(
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                dashboardApi = FakeDashboardApi(ApiResult.Ok(DashboardStats())),
                streamApi = FakeStreamApi(),
                commandsApi = FakeCommandsApi(ApiResult.Ok(commands)),
                communityApi = FakeCommunityApi(),
            )

        controller.load()

        val state: HomeState = controller.state.value
        assertTrue(state is HomeState.Ready)
        val top: List<CommandSummary> = (state as HomeState.Ready).topCommands
        assertEquals(5, top.size)
        assertEquals("!f", top[0].name)
        assertEquals("!a", top[1].name)
        assertEquals("!b", top[2].name)
        assertEquals("!e", top[3].name)
        assertEquals("!d", top[4].name)
    }

    @Test
    fun updateStreamInfo_merges_the_saved_title_into_the_banner_stats_and_stream_info() = runTest {
        // The regression: the PUT echoed the saved title, but only streamInfo was merged — the live banner
        // renders stats.streamTitle, so the old title stayed on screen until a full page reload.
        val controller =
            HomeController(
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                dashboardApi = FakeDashboardApi(
                    ApiResult.Ok(DashboardStats(streamTitle = "Old title", gameName = "Old game"))
                ),
                streamApi = FakeStreamApi(
                    updateResult = ApiResult.Ok(StreamInfo(title = "New title", gameName = "New game"))
                ),
                commandsApi = FakeCommandsApi(),
                communityApi = FakeCommunityApi(),
            )
        controller.load()

        controller.updateStreamInfo(title = "New title", gameName = "New game", tags = null)

        val state: HomeState = controller.state.value
        assertTrue(state is HomeState.Ready)
        val ready: HomeState.Ready = state as HomeState.Ready
        assertEquals("New title", ready.stats.streamTitle)
        assertEquals("New game", ready.stats.gameName)
        assertEquals("New title", ready.streamInfo?.title)
        assertNull(ready.streamError)
    }

    @Test
    fun hub_stream_info_changed_updates_the_banner_without_a_reload() = runTest {
        // The regression's second half: the hub pushed StreamInfoChanged on channel.update, but the client
        // dropped it (unmodelled target) — an edit by another operator or on Twitch itself never showed live.
        val controller =
            HomeController(
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                dashboardApi = FakeDashboardApi(
                    ApiResult.Ok(DashboardStats(streamTitle = "Old title", gameName = "Old game"))
                ),
                streamApi = FakeStreamApi(),
                commandsApi = FakeCommandsApi(),
                communityApi = FakeCommunityApi(),
            )
        controller.load()

        // Collect on an unconfined test dispatcher so the subscription is live immediately (see ChatControllerTest).
        val events = MutableSharedFlow<HubEvent>(extraBufferCapacity = 16)
        backgroundScope.launch(UnconfinedTestDispatcher(testScheduler)) { controller.subscribeToHub(events) }

        events.emit(
            HubEvent.StreamInfoChanged(
                HubStreamInfoChanged(
                    broadcasterId = "ch1",
                    broadcasterDisplayName = "Streamer",
                    title = "Pushed title",
                    gameName = "Pushed game",
                )
            )
        )

        val state: HomeState = controller.state.value
        assertTrue(state is HomeState.Ready)
        val ready: HomeState.Ready = state as HomeState.Ready
        assertEquals("Pushed title", ready.stats.streamTitle)
        assertEquals("Pushed game", ready.stats.gameName)
        assertEquals("Pushed title", ready.streamInfo?.title)
    }

    @Test
    fun load_survives_commands_api_failure_and_shows_empty_top_commands() = runTest {
        val controller =
            HomeController(
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                dashboardApi = FakeDashboardApi(ApiResult.Ok(DashboardStats())),
                streamApi = FakeStreamApi(),
                commandsApi = FakeCommandsApi(ApiResult.Failure(ApiError(500, "ERR", "commands unavailable"))),
                communityApi = FakeCommunityApi(),
            )

        controller.load()

        val state: HomeState = controller.state.value
        assertTrue(state is HomeState.Ready)
        assertTrue((state as HomeState.Ready).topCommands.isEmpty())
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

private class FakeStreamApi(
    private val infoResult: ApiResult<StreamInfo> = ApiResult.Ok(StreamInfo()),
    private val updateResult: ApiResult<StreamInfo> = ApiResult.Ok(StreamInfo()),
) : StreamApi {
    override suspend fun info(channelId: String): ApiResult<StreamInfo> = infoResult
    override suspend fun update(channelId: String, update: StreamInfoUpdate): ApiResult<StreamInfo> =
        updateResult
    override suspend fun searchCategories(channelId: String, query: String): ApiResult<List<Category>> =
        ApiResult.Ok(emptyList())
}

private class FakeCommunityApi : CommunityApi {
    override suspend fun searchViewers(
        channelId: String,
        query: String,
        limit: Int,
    ): ApiResult<List<ViewerOption>> = ApiResult.Ok(emptyList())

    override suspend fun members(channelId: String) = error("stub")
    override suspend fun membersPage(
        channelId: String,
        role: String?,
        page: Int,
        pageSize: Int,
        cursor: String?,
    ) = error("stub")
    override suspend fun topChatters(channelId: String) = error("stub")
    override suspend fun setTrust(channelId: String, userId: String, level: String) = error("stub")
    override suspend fun ban(channelId: String, userId: String, reason: String) = error("stub")
    override suspend fun unban(channelId: String, userId: String) = error("stub")
    override suspend fun addVip(channelId: String, userId: String) = error("stub")
    override suspend fun removeVip(channelId: String, userId: String) = error("stub")
    override suspend fun shoutout(channelId: String, targetTwitchUserId: String) = error("stub")
}

private class FakeCommandsApi(
    private val result: ApiResult<List<CommandSummary>> = ApiResult.Ok(emptyList()),
) : CommandsApi {
    override suspend fun list(channelId: String): ApiResult<List<CommandSummary>> = result
    override suspend fun create(channelId: String, body: CreateCommandBody): ApiResult<Unit> =
        ApiResult.Ok(Unit)
    override suspend fun update(
        channelId: String,
        commandName: String,
        body: UpdateCommandBody,
    ): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun delete(channelId: String, commandName: String): ApiResult<Unit> =
        ApiResult.Ok(Unit)
}

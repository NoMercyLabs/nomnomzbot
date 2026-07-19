// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.analytics.state

import bot.nomnomz.dashboard.core.network.AnalyticsApi
import bot.nomnomz.dashboard.core.network.AnalyticsSummary
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.DailyMetricRow
import bot.nomnomz.dashboard.core.network.StreamAnalytics
import bot.nomnomz.dashboard.core.network.StreamListItem
import bot.nomnomz.dashboard.core.network.TopViewerEntry
import bot.nomnomz.dashboard.core.network.ViewerAnalyticsProfile
import bot.nomnomz.dashboard.core.network.ViewerEngagementDay
import bot.nomnomz.dashboard.core.network.ViewerProfileListEntry
import bot.nomnomz.dashboard.core.network.ViewerProfilePage
import bot.nomnomz.dashboard.core.network.WatchStreak
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the Analytics page state machine the screen renders: resolve the active channel, then surface the
// channel summary — or an error if either step fails. The screen is a pure projection of this, so testing it
// proves the page shows real data (no fabricated counts) and degrades cleanly.
class AnalyticsControllerTest {

    @Test
    fun load_surfaces_the_channel_summary_on_success() = runTest {
        val controller =
            AnalyticsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeAnalyticsApi(
                    ApiResult.Ok(
                        AnalyticsSummary(
                            totalMessages = 1200,
                            newFollowers = 45,
                            newSubscribers = 7,
                            bitsCheered = 5000,
                            commandsRun = 300,
                            redemptionsCount = 12,
                            songRequests = 18,
                            currencyEarnedTotal = 99_000,
                            currencySpentTotal = 40_000,
                            peakViewers = 256,
                        )
                    )
                ),
            )

        controller.load()

        val state: AnalyticsState = controller.state.value
        assertTrue(state is AnalyticsState.Ready)
        val summary: AnalyticsSummary = (state as AnalyticsState.Ready).summary
        assertEquals(1200, summary.totalMessages)
        assertEquals(45, summary.newFollowers)
        assertEquals(7, summary.newSubscribers)
        assertEquals(5000, summary.bitsCheered)
        assertEquals(256, summary.peakViewers)
    }

    @Test
    fun load_addresses_the_resolved_channel_over_a_valid_window() = runTest {
        val analyticsApi = FakeAnalyticsApi(ApiResult.Ok(AnalyticsSummary()))
        val controller =
            AnalyticsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), analyticsApi)

        controller.load()

        // The summary is requested for the resolved channel over an inclusive yyyy-MM-dd window the backend
        // accepts: from <= to. Asserting this proves the controller addresses real data, not a fixed stub path.
        assertEquals("ch1", analyticsApi.requestedChannelId)
        val from: String = requireNotNull(analyticsApi.requestedFrom)
        val to: String = requireNotNull(analyticsApi.requestedTo)
        assertTrue(ISO_DATE.matches(from), "from is not yyyy-MM-dd: $from")
        assertTrue(ISO_DATE.matches(to), "to is not yyyy-MM-dd: $to")
        assertTrue(from <= to, "from ($from) must be on or before to ($to)")
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            AnalyticsController(
                FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                FakeAnalyticsApi(ApiResult.Ok(AnalyticsSummary())),
            )

        controller.load()

        assertTrue(controller.state.value is AnalyticsState.Error)
    }

    @Test
    fun load_errors_when_the_summary_call_fails() = runTest {
        val controller =
            AnalyticsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeAnalyticsApi(ApiResult.Failure(ApiError(500, "ERR", "boom"))),
            )

        controller.load()

        assertTrue(controller.state.value is AnalyticsState.Error)
    }

    @Test
    fun load_includes_daily_rows_and_top_viewers_in_ready_state() = runTest {
        val daily: List<DailyMetricRow> = listOf(
            DailyMetricRow(
                activityDate = "2026-06-01",
                uniqueChatters = 10,
                totalMessages = 500,
                newFollowers = 3,
                peakViewers = 42,
            ),
        )
        val top: List<TopViewerEntry> = listOf(
            TopViewerEntry(viewerUserId = "u1", displayName = "Alice", metricValue = 350),
            TopViewerEntry(viewerUserId = "u2", displayName = "Bob", metricValue = 150),
        )
        val controller =
            AnalyticsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeAnalyticsApi(
                    summaryResult = ApiResult.Ok(AnalyticsSummary(totalMessages = 500)),
                    dailyResult = ApiResult.Ok(daily),
                    topViewersResult = ApiResult.Ok(top),
                ),
            )

        controller.load()

        val state: AnalyticsState = controller.state.value
        assertTrue(state is AnalyticsState.Ready)
        val ready: AnalyticsState.Ready = state as AnalyticsState.Ready
        assertEquals(1, ready.daily.size)
        assertEquals("2026-06-01", ready.daily[0].activityDate)
        assertEquals(42, ready.daily[0].peakViewers)
        assertEquals(2, ready.topViewers.size)
        assertEquals("Alice", ready.topViewers[0].displayName)
        assertEquals(350L, ready.topViewers[0].metricValue)
    }

    @Test
    fun load_tolerates_daily_and_top_viewer_failures_and_stays_ready() = runTest {
        val controller =
            AnalyticsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeAnalyticsApi(
                    summaryResult = ApiResult.Ok(AnalyticsSummary(totalMessages = 1)),
                    dailyResult = ApiResult.Failure(ApiError(500, "ERR", "daily down")),
                    topViewersResult = ApiResult.Failure(ApiError(500, "ERR", "top down")),
                ),
            )

        controller.load()

        // Summary success → Ready even when daily + topViewers fail; those lists are just empty.
        val state: AnalyticsState = controller.state.value
        assertTrue(state is AnalyticsState.Ready)
        val ready: AnalyticsState.Ready = state as AnalyticsState.Ready
        assertTrue(ready.daily.isEmpty())
        assertTrue(ready.topViewers.isEmpty())
    }

    @Test
    fun load_includes_the_stream_history_for_the_picker() = runTest {
        val streams: List<StreamListItem> =
            listOf(
                StreamListItem(streamId = "s2", title = "Latest", startedAt = "2026-07-16T20:00:00Z"),
                StreamListItem(streamId = "s1", title = "Older", startedAt = "2026-07-10T20:00:00Z"),
            )
        val controller =
            AnalyticsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeAnalyticsApi(
                    summaryResult = ApiResult.Ok(AnalyticsSummary()),
                    streamsResult = ApiResult.Ok(streams),
                ),
            )

        controller.load()

        val ready: AnalyticsState.Ready = controller.state.value as AnalyticsState.Ready
        assertEquals(listOf("s2", "s1"), ready.streams.map { it.streamId })
        // No stream selected yet — the page starts on the all-time view.
        assertEquals(null, ready.selectedStreamId)
        assertEquals(null, ready.streamDetail)
    }

    @Test
    fun select_stream_folds_that_streams_numbers_then_all_time_clears_them() = runTest {
        val detail =
            StreamAnalytics(
                streamId = "s1",
                title = "Big raid night",
                totalMessages = 4200,
                uniqueChatters = 310,
                newFollowers = 55,
                cheersCount = 900,
            )
        val analyticsApi =
            FakeAnalyticsApi(
                summaryResult = ApiResult.Ok(AnalyticsSummary(totalMessages = 10)),
                streamsResult = ApiResult.Ok(listOf(StreamListItem(streamId = "s1", title = "Big raid night"))),
                streamDetailResult = ApiResult.Ok(detail),
            )
        val controller =
            AnalyticsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), analyticsApi)

        controller.load()
        controller.selectStream("s1")

        // The detail was fetched for the chosen stream and folded into the Ready state.
        assertEquals("s1", analyticsApi.requestedStreamId)
        val selected: AnalyticsState.Ready = controller.state.value as AnalyticsState.Ready
        assertEquals("s1", selected.selectedStreamId)
        assertEquals(4200L, selected.streamDetail?.totalMessages)
        assertEquals(310, selected.streamDetail?.uniqueChatters)
        assertEquals(900L, selected.streamDetail?.cheersCount)

        // Switching back to all-time drops the per-stream detail.
        controller.selectStream(null)
        val allTime: AnalyticsState.Ready = controller.state.value as AnalyticsState.Ready
        assertEquals(null, allTime.selectedStreamId)
        assertEquals(null, allTime.streamDetail)
    }

    @Test
    fun load_viewers_surfaces_the_first_page_of_the_viewer_list() = runTest {
        val page: ViewerProfilePage =
            ViewerProfilePage(
                data =
                    listOf(
                        ViewerProfileListEntry(
                            viewerUserId = "iu1",
                            displayName = "Nibbles",
                            totalWatchSeconds = 7200,
                            totalMessages = 340,
                            lastSeenAt = "2026-07-18T20:00:00Z",
                        )
                    ),
                nextPage = 2,
                hasMore = true,
                total = 51,
            )
        val analyticsApi = FakeAnalyticsApi(summaryResult = ApiResult.Ok(AnalyticsSummary()), viewersResult = ApiResult.Ok(page))
        val controller =
            AnalyticsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), analyticsApi)

        controller.loadViewers()

        // The list resolved the channel, defaulted to the Watch sort + page 1, and projected the row + paging.
        assertEquals("ch1", analyticsApi.requestedChannelId)
        assertEquals("Watch", analyticsApi.requestedViewerSort)
        assertEquals(1, analyticsApi.requestedViewerPage)
        val ready: ViewerListState.Ready = controller.viewers.value as ViewerListState.Ready
        assertEquals(1, ready.viewers.size)
        assertEquals("Nibbles", ready.viewers.first().displayName)
        assertEquals(340L, ready.viewers.first().totalMessages)
        assertTrue(ready.hasMore)
        assertEquals(51, ready.total)
    }

    @Test
    fun next_viewers_page_advances_the_requested_page() = runTest {
        val analyticsApi =
            FakeAnalyticsApi(
                summaryResult = ApiResult.Ok(AnalyticsSummary()),
                viewersResult = ApiResult.Ok(ViewerProfilePage(hasMore = true)),
            )
        val controller =
            AnalyticsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), analyticsApi)

        controller.loadViewers()
        controller.nextViewersPage()

        assertEquals(2, analyticsApi.requestedViewerPage)
        val ready: ViewerListState.Ready = controller.viewers.value as ViewerListState.Ready
        assertEquals(2, ready.page)
        assertTrue(ready.hasPrev)
    }

    @Test
    fun open_viewer_loads_the_profile_by_internal_user_id() = runTest {
        val profile: ViewerAnalyticsProfile =
            ViewerAnalyticsProfile(
                viewerUserId = "iu1",
                totalMessages = 999,
                totalRedemptions = 4,
                isSubscriber = true,
                subTier = "2000",
            )
        val analyticsApi =
            FakeAnalyticsApi(summaryResult = ApiResult.Ok(AnalyticsSummary()), profileResult = ApiResult.Ok(profile))
        val controller =
            AnalyticsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), analyticsApi)

        controller.openViewer("iu1", "Nibbles")

        // The drill-down addressed the viewer by their internal id and folded the profile into the detail state.
        assertEquals("iu1", analyticsApi.requestedViewerId)
        val detail: ViewerDetailState = assertNotNull(controller.viewerDetail.value)
        assertEquals("Nibbles", detail.displayName)
        assertEquals(999L, detail.profile?.totalMessages)
        assertTrue(detail.profile?.isSubscriber == true)
        assertTrue(!detail.loading)
        assertTrue(!detail.error)

        controller.closeViewer()
        assertEquals(null, controller.viewerDetail.value)
    }

    private companion object {
        val ISO_DATE = Regex("""\d{4}-\d{2}-\d{2}""")
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

private class FakeAnalyticsApi(
    private val summaryResult: ApiResult<AnalyticsSummary>,
    private val dailyResult: ApiResult<List<DailyMetricRow>> = ApiResult.Ok(emptyList()),
    private val topViewersResult: ApiResult<List<TopViewerEntry>> = ApiResult.Ok(emptyList()),
    private val streamsResult: ApiResult<List<StreamListItem>> = ApiResult.Ok(emptyList()),
    private val streamDetailResult: ApiResult<StreamAnalytics> = ApiResult.Ok(StreamAnalytics()),
    private val viewersResult: ApiResult<ViewerProfilePage> = ApiResult.Ok(ViewerProfilePage()),
    private val profileResult: ApiResult<ViewerAnalyticsProfile> = ApiResult.Ok(ViewerAnalyticsProfile()),
) : AnalyticsApi {
    var requestedChannelId: String? = null
    var requestedFrom: String? = null
    var requestedTo: String? = null
    var requestedStreamId: String? = null
    var requestedViewerSearch: String? = null
    var requestedViewerSort: String? = null
    var requestedViewerPage: Int? = null
    var requestedViewerId: String? = null

    constructor(result: ApiResult<AnalyticsSummary>) : this(summaryResult = result)

    override suspend fun summary(
        channelId: String,
        from: String,
        to: String,
    ): ApiResult<AnalyticsSummary> {
        requestedChannelId = channelId
        requestedFrom = from
        requestedTo = to
        return summaryResult
    }

    override suspend fun daily(
        channelId: String,
        from: String,
        to: String,
    ): ApiResult<List<DailyMetricRow>> = dailyResult

    override suspend fun streams(channelId: String): ApiResult<List<StreamListItem>> = streamsResult

    override suspend fun streamDetail(
        channelId: String,
        streamId: String,
    ): ApiResult<StreamAnalytics> {
        requestedStreamId = streamId
        return streamDetailResult
    }

    override suspend fun topViewers(
        channelId: String,
        metric: String,
        from: String,
        to: String,
        top: Int,
    ): ApiResult<List<TopViewerEntry>> = topViewersResult

    override suspend fun listViewers(
        channelId: String,
        search: String?,
        sort: String,
        followersOnly: Boolean?,
        subscribersOnly: Boolean?,
        page: Int,
        pageSize: Int,
    ): ApiResult<ViewerProfilePage> {
        requestedChannelId = channelId
        requestedViewerSearch = search
        requestedViewerSort = sort
        requestedViewerPage = page
        return viewersResult
    }

    override suspend fun viewerProfile(
        channelId: String,
        viewerUserId: String,
    ): ApiResult<ViewerAnalyticsProfile> {
        requestedViewerId = viewerUserId
        return profileResult
    }

    override suspend fun viewerEngagement(
        channelId: String,
        viewerUserId: String,
        from: String,
        to: String,
    ): ApiResult<List<ViewerEngagementDay>> = ApiResult.Ok(emptyList())

    override suspend fun viewerStreak(
        channelId: String,
        viewerUserId: String,
    ): ApiResult<WatchStreak> = ApiResult.Ok(WatchStreak())

    override suspend fun setAnalyticsOptOut(
        channelId: String,
        viewerUserId: String,
        optedOut: Boolean,
    ): ApiResult<Unit> = ApiResult.Ok(Unit)
}

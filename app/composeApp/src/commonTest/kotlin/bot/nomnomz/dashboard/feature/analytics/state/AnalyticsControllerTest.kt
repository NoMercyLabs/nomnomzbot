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
import kotlin.test.Test
import kotlin.test.assertEquals
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
}

private class FakeAnalyticsApi(private val result: ApiResult<AnalyticsSummary>) : AnalyticsApi {
    var requestedChannelId: String? = null
    var requestedFrom: String? = null
    var requestedTo: String? = null

    override suspend fun summary(
        channelId: String,
        from: String,
        to: String,
    ): ApiResult<AnalyticsSummary> {
        requestedChannelId = channelId
        requestedFrom = from
        requestedTo = to
        return result
    }
}

// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.pipelines.state

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.PipelineExecutionDetail
import bot.nomnomz.dashboard.core.network.PipelineExecutionPage
import bot.nomnomz.dashboard.core.network.PipelineExecutionStatus
import bot.nomnomz.dashboard.core.network.PipelineExecutionStepLog
import bot.nomnomz.dashboard.core.network.PipelineExecutionSummary
import bot.nomnomz.dashboard.core.network.PipelineExecutionsApi
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the run-history state machine the debugging surface renders: a paged, newest-first list of the
// channel's real runs that pages forward on real ids (not merely a changed count) and re-queries when the
// failures-only filter flips, plus a detail read that identifies the FAILING step of a PartiallyFailed run
// and its error text — the exact fact a streamer needs to fix a misbehaving command.
class PipelineExecutionHistoryControllerTest {

    @Test
    fun load_surfaces_the_first_page_newest_first() = runTest {
        val controller =
            PipelineExecutionHistoryController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeExecutionsApi(pages = listOf(pageOf(listOf(runSummary(id = 2), runSummary(id = 1))))),
            )

        controller.load()

        val state: PipelineExecutionHistoryState = controller.state.value
        assertTrue(state is PipelineExecutionHistoryState.List)
        val ids: List<Long> = (state as PipelineExecutionHistoryState.List).runs.map { it.id }
        assertEquals(listOf(2L, 1L), ids)
    }

    @Test
    fun next_page_loads_the_second_pages_ids() = runTest {
        val api =
            FakeExecutionsApi(
                pages =
                    listOf(
                        pageOf(runs = listOf(runSummary(id = 4), runSummary(id = 3)), hasMore = true, nextPage = 2),
                        pageOf(runs = listOf(runSummary(id = 2), runSummary(id = 1)), hasMore = false),
                    )
            )
        val controller = PipelineExecutionHistoryController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()

        controller.nextPage()

        val state: PipelineExecutionHistoryState.List = controller.state.value as PipelineExecutionHistoryState.List
        assertEquals(listOf(2L, 1L), state.runs.map { it.id })
        assertEquals(2, state.page)
        assertTrue(state.hasPrev)
        assertEquals(listOf(1, 2), api.pageCalls.map { it.pageNumber })
    }

    @Test
    fun toggling_failures_only_requeries_and_holds_only_failing_runs() = runTest {
        val api =
            FakeExecutionsApi(
                pages =
                    listOf(
                        pageOf(runs = listOf(runSummary(id = 1, status = PipelineExecutionStatus.Succeeded))),
                        pageOf(runs = listOf(runSummary(id = 2, status = PipelineExecutionStatus.Failed))),
                    )
            )
        val controller = PipelineExecutionHistoryController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()

        controller.setFailuresOnly(true)

        val state: PipelineExecutionHistoryState.List = controller.state.value as PipelineExecutionHistoryState.List
        assertTrue(state.failuresOnly)
        assertEquals(1, state.runs.size)
        assertEquals(PipelineExecutionStatus.Failed, state.runs.single().status)
        assertEquals(1, state.page)
        assertTrue(api.pageCalls.last().failuresOnly)
    }

    @Test
    fun run_detail_identifies_the_failing_step_and_its_error_for_a_partially_failed_run() = runTest {
        val detail =
            PipelineExecutionDetail(
                id = 42,
                pipelineId = "p1",
                triggerKind = "command",
                status = PipelineExecutionStatus.PartiallyFailed,
                stepLogs =
                    listOf(
                        PipelineExecutionStepLog(stepIndex = 0, actionType = "send_message", succeeded = true),
                        PipelineExecutionStepLog(
                            stepIndex = 1,
                            actionType = "shoutout",
                            succeeded = false,
                            errorMessage = "target channel not found",
                        ),
                        PipelineExecutionStepLog(stepIndex = 2, actionType = "wait", succeeded = true),
                    ),
            )
        val controller =
            PipelineExecutionHistoryController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeExecutionsApi(pages = listOf(pageOf(emptyList())), detail = ApiResult.Ok(detail)),
            )
        controller.load()

        controller.openRun(42)

        val state: PipelineExecutionHistoryState = controller.state.value
        assertTrue(state is PipelineExecutionHistoryState.Detail)
        val failing = (state as PipelineExecutionHistoryState.Detail).run.failingStep
        assertEquals(1, failing?.stepIndex)
        assertEquals("shoutout", failing?.actionType)
        assertEquals("target channel not found", failing?.errorMessage)
    }

    @Test
    fun a_clean_run_has_no_failing_step() = runTest {
        val detail =
            PipelineExecutionDetail(
                id = 7,
                status = PipelineExecutionStatus.Succeeded,
                stepLogs = listOf(PipelineExecutionStepLog(stepIndex = 0, actionType = "send_message", succeeded = true)),
            )
        assertNull(detail.failingStep)
    }

    // ── Fakes ─────────────────────────────────────────────────────────────────

    private fun runSummary(id: Long, status: String = PipelineExecutionStatus.Succeeded): PipelineExecutionSummary =
        PipelineExecutionSummary(id = id, pipelineId = "p1", triggerKind = "command", status = status)

    private fun pageOf(
        runs: List<PipelineExecutionSummary>,
        hasMore: Boolean = false,
        nextPage: Int? = null,
    ): PipelineExecutionPage = PipelineExecutionPage(data = runs, hasMore = hasMore, nextPage = nextPage, total = runs.size)

    private data class PageCall(val pageNumber: Int, val failuresOnly: Boolean)

    private class FakeExecutionsApi(
        private val pages: List<PipelineExecutionPage>,
        private val detail: ApiResult<PipelineExecutionDetail> = ApiResult.Failure(ApiError(404, "NOT_FOUND", "n/a")),
    ) : PipelineExecutionsApi {
        val pageCalls: MutableList<PageCall> = mutableListOf()
        private var callIndex: Int = 0

        override suspend fun page(
            channelId: String,
            page: Int,
            pageSize: Int,
            failuresOnly: Boolean,
        ): ApiResult<PipelineExecutionPage> {
            pageCalls.add(PageCall(page, failuresOnly))
            val index: Int = minOf(callIndex, pages.lastIndex)
            callIndex += 1
            return ApiResult.Ok(pages[index])
        }

        override suspend fun get(channelId: String, id: Long): ApiResult<PipelineExecutionDetail> = detail
    }

    private class FakeChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
        override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result

        override suspend fun list(): ApiResult<List<ChannelSummary>> = ApiResult.Ok(emptyList())

        override suspend fun moderatedChannels(): ApiResult<List<bot.nomnomz.dashboard.core.network.ModeratedChannel>> =
            ApiResult.Ok(emptyList())

        override suspend fun join(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

        override suspend fun leave(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

        override suspend fun reset(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

        override suspend fun deleteChannel(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

        override suspend fun channelScopes(
            channelId: String
        ): ApiResult<bot.nomnomz.dashboard.core.network.ChannelScopesResponse> =
            ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "n/a"))

        override suspend fun startChannelBotConnect(
            channelId: String
        ): ApiResult<bot.nomnomz.dashboard.core.network.OAuthStart> =
            ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "n/a"))

        override suspend fun channelBotStatus(
            channelId: String
        ): ApiResult<bot.nomnomz.dashboard.core.network.BotStatus> =
            ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "n/a"))

        override suspend fun disconnectChannelBot(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    }
}

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

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.PipelineExecutionDetail
import bot.nomnomz.dashboard.core.network.PipelineExecutionPage
import bot.nomnomz.dashboard.core.network.PipelineExecutionSummary
import bot.nomnomz.dashboard.core.network.PipelineExecutionsApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The pipeline run-history state-holder (S008c-read-b, frontend-ia.md §3 — the Pipelines group). Backs a
// debugging surface under feature/pipelines: a paged, newest-first list of the channel's real pipeline runs
// (H.4 PipelineExecution, no fabricated rows), a failures-only filter, and a per-run detail view that
// identifies the FAILING step and its error text for a `PartiallyFailed`/`Failed` run. Read-only — gated by
// `pipelines:read` (PipelineExecutionHistoryAccess); no writes, no reload-on-hub-push (a run is immutable
// once it lands, unlike the mutable config domains).
class PipelineExecutionHistoryController(
    private val channelsApi: ChannelsApi,
    private val executionsApi: PipelineExecutionsApi,
) {
    private val _state: MutableStateFlow<PipelineExecutionHistoryState> =
        MutableStateFlow(PipelineExecutionHistoryState.Loading)

    /** The run-history render state: loading / list (ready/empty) / a run's detail / error. */
    val state: StateFlow<PipelineExecutionHistoryState> = _state.asStateFlow()

    private var channelId: String? = null
    private var page: Int = 1
    private var failuresOnly: Boolean = false

    /** Resolve the channel and load the first page of runs. */
    suspend fun load() {
        if (_state.value !is PipelineExecutionHistoryState.List) {
            _state.value = PipelineExecutionHistoryState.Loading
        }
        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = PipelineExecutionHistoryState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id
        page = 1
        loadPage(channel.id)
    }

    private suspend fun loadPage(channel: String) {
        when (
            val result: ApiResult<PipelineExecutionPage> =
                executionsApi.page(channel, page, PageSize, failuresOnly)
        ) {
            is ApiResult.Failure -> _state.value = PipelineExecutionHistoryState.Error(result.error.message)
            is ApiResult.Ok -> {
                val runsPage: PipelineExecutionPage = result.value
                _state.value =
                    PipelineExecutionHistoryState.List(
                        runs = runsPage.data,
                        page = page,
                        hasPrev = page > 1,
                        hasMore = runsPage.hasMore,
                        total = runsPage.total,
                        failuresOnly = failuresOnly,
                    )
            }
        }
    }

    /** Toggle the failures-only filter, resetting to the first page, and re-query. */
    suspend fun setFailuresOnly(enabled: Boolean) {
        failuresOnly = enabled
        page = 1
        val channel: String = channelId ?: return
        loadPage(channel)
    }

    /** Advance to the next page. The screen only calls this while `hasMore` is true. */
    suspend fun nextPage() {
        val channel: String = channelId ?: return
        page += 1
        loadPage(channel)
    }

    /** Step back to the previous page. A no-op on the first page. */
    suspend fun prevPage() {
        val channel: String = channelId ?: return
        if (page <= 1) return
        page -= 1
        loadPage(channel)
    }

    /** Open a run's detail — its ordered step logs, with the failing step identified. */
    suspend fun openRun(id: Long) {
        val channel: String = channelId ?: return
        _state.value = PipelineExecutionHistoryState.Loading
        when (val result: ApiResult<PipelineExecutionDetail> = executionsApi.get(channel, id)) {
            is ApiResult.Failure -> _state.value = PipelineExecutionHistoryState.Error(result.error.message)
            is ApiResult.Ok -> _state.value = PipelineExecutionHistoryState.Detail(result.value)
        }
    }

    /** Return from the detail view to the run list, on the page the caller left. */
    suspend fun closeRun() {
        val channel: String = channelId ?: return
        loadPage(channel)
    }

    private companion object {
        const val PageSize: Int = 25
    }
}

/** The run-history page's render state. */
sealed interface PipelineExecutionHistoryState {
    data object Loading : PipelineExecutionHistoryState

    /**
     * A page of the channel's runs, newest-first. [page] is the 1-based page number; [hasPrev]/[hasMore]
     * drive the prev/next controls, [total] the "of N" count when the backend knows it, and [failuresOnly]
     * mirrors the active filter so the toggle control reflects it.
     */
    data class List(
        val runs: kotlin.collections.List<PipelineExecutionSummary>,
        val page: Int = 1,
        val hasPrev: Boolean = false,
        val hasMore: Boolean = false,
        val total: Int? = null,
        val failuresOnly: Boolean = false,
    ) : PipelineExecutionHistoryState

    /** One run's full detail, including its ordered step logs. */
    data class Detail(val run: PipelineExecutionDetail) : PipelineExecutionHistoryState

    data class Error(val detail: String) : PipelineExecutionHistoryState
}

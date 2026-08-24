// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.network

import kotlinx.serialization.Serializable

// The typed pipeline run-history facade (S008c-read-b) — lets the dashboard show WHY a command/event-response
// pipeline misbehaved: a paged, newest-first list of runs (H.4 PipelineExecution) plus a per-run detail read
// carrying the ordered step logs that pinpoint the failing step. Read-only; gated by `pipelines:read`.
//
// Backend routes (PipelineExecutionsController, base `/api/v1/channels/{channelId}/pipeline-executions`):
//   GET .            →  PaginatedResponse<PipelineExecutionSummaryDto>   (?page=&pageSize=&failuresOnly=)
//   GET ./{id}       →  StatusResponseDto<PipelineExecutionDetailDto>
interface PipelineExecutionsApi {
    /**
     * One page of the channel's pipeline runs, newest-first. [failuresOnly] restricts to non-success outcomes
     * (backend `?failuresOnly=`).
     */
    suspend fun page(
        channelId: String,
        page: Int,
        pageSize: Int,
        failuresOnly: Boolean,
    ): ApiResult<PipelineExecutionPage>

    /** One run's full detail, including its ordered per-step logs. */
    suspend fun get(channelId: String, id: Long): ApiResult<PipelineExecutionDetail>
}

class RestPipelineExecutionsApi(private val client: ApiClient) : PipelineExecutionsApi {
    override suspend fun page(
        channelId: String,
        page: Int,
        pageSize: Int,
        failuresOnly: Boolean,
    ): ApiResult<PipelineExecutionPage> =
        client.getDirect(
            "api/v1/channels/$channelId/pipeline-executions?page=$page&pageSize=$pageSize&failuresOnly=$failuresOnly"
        )

    override suspend fun get(channelId: String, id: Long): ApiResult<PipelineExecutionDetail> =
        client.getEnvelope("api/v1/channels/$channelId/pipeline-executions/$id")
}

/**
 * One page of the run-history list (backend `PaginatedResponse<PipelineExecutionSummaryDto>`). Flat `data`
 * plus the page continuation, mirroring [QuotePage]'s shape for the same reason — the screen pages through
 * this with next/prev.
 */
@Serializable
data class PipelineExecutionPage(
    val data: List<PipelineExecutionSummary> = emptyList(),
    val nextPage: Int? = null,
    val hasMore: Boolean = false,
    val total: Int? = null,
)

/**
 * A run's list-view summary (backend `PipelineExecutionSummaryDto`): which [pipelineId] ran, what fired it
 * ([triggerKind]), the outcome [status] (`Succeeded` / `PartiallyFailed` / `Failed`, matching the backend's
 * H.4 status enum), timing, and — for a failed/partially-failed run — the top-level [errorMessage].
 */
@Serializable
data class PipelineExecutionSummary(
    val id: Long = 0,
    val pipelineId: String = "",
    val triggerKind: String = "",
    val status: String = "",
    val hostCallCount: Int = 0,
    val durationMs: Int = 0,
    val errorMessage: String? = null,
    val startedAt: String = "",
    val completedAt: String? = null,
)

/** One step's outcome within a run (backend `PipelineExecutionStepLogDto`) — the unit the detail view marks as failing. */
@Serializable
data class PipelineExecutionStepLog(
    val stepIndex: Int = 0,
    val actionType: String = "",
    val succeeded: Boolean = true,
    val durationMs: Int = 0,
    val errorMessage: String? = null,
)

/** A run's full detail (backend `PipelineExecutionDetailDto`) — the summary fields plus its ordered [stepLogs]. */
@Serializable
data class PipelineExecutionDetail(
    val id: Long = 0,
    val pipelineId: String = "",
    val triggerKind: String = "",
    val status: String = "",
    val hostCallCount: Int = 0,
    val durationMs: Int = 0,
    val errorMessage: String? = null,
    val startedAt: String = "",
    val completedAt: String? = null,
    val stepLogs: List<PipelineExecutionStepLog> = emptyList(),
) {
    /** The first step that did not succeed — the run's FAILING step, or null on a clean success. */
    val failingStep: PipelineExecutionStepLog?
        get() = stepLogs.firstOrNull { !it.succeeded }
}

/** Backend H.4 `PipelineExecutionStatus` values, as carried on the wire (string enum). */
object PipelineExecutionStatus {
    const val Succeeded: String = "Succeeded"
    const val PartiallyFailed: String = "PartiallyFailed"
    const val Failed: String = "Failed"
}

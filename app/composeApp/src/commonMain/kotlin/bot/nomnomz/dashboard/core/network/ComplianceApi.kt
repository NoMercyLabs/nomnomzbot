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

// The operator compliance plane's monitor surface (GET /api/v1/compliance/erasure*, gdpr-crypto.md §5.2):
// every subject's GDPR requests, platform-wide, gated server-side on `audit:read`. Subject-initiated
// requests stay on GdprApi; the operator-initiated export lives there too. Re-running a failed erasure
// (POST .../erasure/{id}/retry) is gated on `compliance:erasure`, like the operator erasure itself.

/** Every subject's requests counted by status (backend `ErasureRequestSummaryDto`), from the real ledger. */
@Serializable
data class ErasureRequestSummary(
    val total: Int = 0,
    val pending: Int = 0,
    val running: Int = 0,
    val completed: Int = 0,
    val failed: Int = 0,
    val cancelled: Int = 0,
)

/** The request statuses and kinds the ledger records — the same [VC:enum] sets the backend validates. */
object ErasureRequestStatuses {
    const val RUNNING: String = "running"
    const val COMPLETED: String = "completed"
    const val FAILED: String = "failed"
}

object ErasureRequestTypes {
    const val ERASURE: String = "erasure"
    const val EXPORT: String = "export"
    const val OPT_OUT: String = "opt_out"
}

interface ComplianceApi {
    /** Every subject's requests, newest first; [status] / [requestType] narrow the page (null = all). */
    suspend fun listRequests(
        status: String? = null,
        requestType: String? = null,
        page: Int = 1,
        pageSize: Int = 50,
    ): ApiResult<PaginatedEnvelope<ErasureRequest>>

    suspend fun summary(): ApiResult<ErasureRequestSummary>

    /** The counted blast radius of erasing [subjectUserId] — read before an operator confirms a re-run. */
    suspend fun previewErasure(subjectUserId: String): ApiResult<ErasurePreview>

    /** Re-runs a FAILED erasure request in place; the server refuses (409) any other state. */
    suspend fun retryErasure(erasureRequestId: String): ApiResult<ErasureRequest>
}

class RestComplianceApi(private val client: ApiClient) : ComplianceApi {
    override suspend fun listRequests(
        status: String?,
        requestType: String?,
        page: Int,
        pageSize: Int,
    ): ApiResult<PaginatedEnvelope<ErasureRequest>> {
        val query: String =
            buildString {
                append("page=").append(page).append("&pageSize=").append(pageSize)
                status?.let { append("&status=").append(it.encodeQuery()) }
                requestType?.let { append("&requestType=").append(it.encodeQuery()) }
            }
        // A PaginatedResponse is a flat `{ data: [...] }`, read with getDirect rather than getEnvelope.
        return client.getDirect("api/v1/compliance/erasure?$query")
    }

    override suspend fun summary(): ApiResult<ErasureRequestSummary> =
        client.getEnvelope("api/v1/compliance/erasure/summary")

    override suspend fun previewErasure(subjectUserId: String): ApiResult<ErasurePreview> =
        client.getEnvelope("api/v1/compliance/erasure/preview?subjectUserId=${subjectUserId.encodeQuery()}")

    override suspend fun retryErasure(erasureRequestId: String): ApiResult<ErasureRequest> =
        client.postEnvelope("api/v1/compliance/erasure/$erasureRequestId/retry")
}

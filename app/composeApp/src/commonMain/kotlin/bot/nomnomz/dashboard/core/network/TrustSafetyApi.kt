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

// The platform-wide trust & safety desk (GET/POST /api/v1/admin/trust-safety/*, S-ADMIN-8a). Gated
// server-side on `trust-safety:review` — a key of its own, not implied by user:support:view or plain
// tenant access. Every call requires a justification, which the backend records on the IAM audit row.

import kotlinx.serialization.Serializable

/** One tenant this actor's abuse signal was recorded in — a real, persisted detection, never a guess. */
@Serializable
data class CrossTenantAbuseHit(
    val broadcasterId: String,
    val channelName: String,
    val detectionId: String,
    val confidence: String,
    val outcome: String,
    val reason: String,
    val detectedAt: String,
)

/** One actor whose spam-defence detections were recorded in two or more tenants of this deployment. */
@Serializable
data class CrossTenantAbuseSignal(
    val provider: String,
    val subjectPlatformUserId: String,
    val subjectDisplayName: String,
    val tenantCount: Int,
    val detectionCount: Int,
    val hits: List<CrossTenantAbuseHit> = emptyList(),
)

/**
 * One automatic account action the spam-defence engine took on its own, with the evidence that caused
 * it and its current review state. [reversalPreview] is the blast radius an overturn would cause,
 * computed server-side so the operator sees it BEFORE committing to one.
 */
@Serializable
data class TrustSafetyReviewItem(
    val detectionId: String,
    val broadcasterId: String,
    val channelName: String,
    val subjectPlatformUserId: String,
    val subjectDisplayName: String,
    val provider: String,
    val messageText: String,
    val signals: String,
    val confidence: String,
    val outcome: String,
    val reason: String,
    val detectedAt: String,
    val confirmedAt: String? = null,
    val confirmedByUserId: String? = null,
    val overturnedAt: String? = null,
    val overturnedByUserId: String? = null,
    val reversalPreview: String,
)

interface TrustSafetyApi {
    suspend fun getCrossTenantSignals(justification: String): ApiResult<List<CrossTenantAbuseSignal>>

    suspend fun getReviewQueue(
        justification: String,
        page: Int = 1,
        pageSize: Int = 25,
    ): ApiResult<PaginatedEnvelope<TrustSafetyReviewItem>>

    suspend fun confirm(detectionId: String, justification: String): ApiResult<Unit>

    suspend fun overturn(detectionId: String, justification: String): ApiResult<Unit>
}

class TrustSafetyApiImpl(private val client: ApiClient) : TrustSafetyApi {
    override suspend fun getCrossTenantSignals(
        justification: String,
    ): ApiResult<List<CrossTenantAbuseSignal>> =
        client.getEnvelope(
            "api/v1/admin/trust-safety/cross-tenant-signals?justification=${justification.encodeQuery()}",
        )

    override suspend fun getReviewQueue(
        justification: String,
        page: Int,
        pageSize: Int,
    ): ApiResult<PaginatedEnvelope<TrustSafetyReviewItem>> =
        client.getDirect(
            "api/v1/admin/trust-safety/review-queue?justification=${justification.encodeQuery()}" +
                "&page=$page&pageSize=$pageSize",
        )

    override suspend fun confirm(detectionId: String, justification: String): ApiResult<Unit> =
        client.postUnit(
            "api/v1/admin/trust-safety/review-queue/$detectionId/confirm" +
                "?justification=${justification.encodeQuery()}",
        )

    override suspend fun overturn(detectionId: String, justification: String): ApiResult<Unit> =
        client.postUnit(
            "api/v1/admin/trust-safety/review-queue/$detectionId/overturn" +
                "?justification=${justification.encodeQuery()}",
        )
}
